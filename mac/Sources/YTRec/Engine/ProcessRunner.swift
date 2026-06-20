import Foundation

/// 包一層 Process：逐行回報輸出、支援取消、回傳完整結果
final class ProcessRunner {
    struct Result {
        let exitCode: Int32
        let output: String       // stdout+stderr 合併（最後 200 行）
        let wasCancelled: Bool
    }

    private let process = Process()
    private var cancelled = false
    private let lock = NSLock()

    func cancel(signal: Int32 = SIGINT) {
        lock.lock(); cancelled = true; lock.unlock()
        if process.isRunning {
            kill(process.processIdentifier, signal)
            // 5 秒後還沒退就強制終止
            DispatchQueue.global().asyncAfter(deadline: .now() + 5) { [weak process] in
                if let p = process, p.isRunning { p.terminate() }
            }
        }
    }

    var isCancelled: Bool {
        lock.lock(); defer { lock.unlock() }
        return cancelled
    }

    /// 執行並等待結束。onLine 在背景 queue 被呼叫。
    func run(executable: URL, arguments: [String], currentDir: URL? = nil,
             onLine: ((String) -> Void)? = nil) async -> Result {
        process.executableURL = executable
        process.arguments = arguments
        if let currentDir { process.currentDirectoryURL = currentDir }
        var env = ProcessInfo.processInfo.environment
        if let ffdir = BinaryLocator.url(for: .ffmpeg)?.deletingLastPathComponent().path {
            env["PATH"] = "\(ffdir):" + (env["PATH"] ?? "/usr/bin:/bin")
        }
        process.environment = env

        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe

        let collector = LineCollector(onLine: onLine)
        pipe.fileHandleForReading.readabilityHandler = { handle in
            let data = handle.availableData
            if !data.isEmpty { collector.feed(data) }
        }

        return await withCheckedContinuation { cont in
            process.terminationHandler = { [weak self] p in
                pipe.fileHandleForReading.readabilityHandler = nil
                // 撈出殘留資料
                if let rest = try? pipe.fileHandleForReading.readToEnd(), !rest.isEmpty {
                    collector.feed(rest)
                }
                collector.flush()
                let r = Result(exitCode: p.terminationStatus,
                               output: collector.tail.joined(separator: "\n"),
                               wasCancelled: self?.isCancelled ?? false)
                cont.resume(returning: r)
            }
            do {
                try process.run()
            } catch {
                pipe.fileHandleForReading.readabilityHandler = nil
                cont.resume(returning: Result(exitCode: -1, output: "無法啟動：\(error.localizedDescription)", wasCancelled: false))
            }
        }
    }
}

/// 把資料流切成行（處理 \n 與 \r 進度列），保留最後 200 行
final class LineCollector {
    private var buffer = Data()
    private(set) var tail: [String] = []
    private let onLine: ((String) -> Void)?
    private let queue = DispatchQueue(label: "lcf.linecollector")

    init(onLine: ((String) -> Void)?) { self.onLine = onLine }

    func feed(_ data: Data) {
        queue.async { [self] in
            buffer.append(data)
            while let idx = buffer.firstIndex(where: { $0 == 0x0A || $0 == 0x0D }) {
                let lineData = buffer.subdata(in: buffer.startIndex..<idx)
                buffer.removeSubrange(buffer.startIndex...idx)
                emit(lineData)
            }
        }
    }

    func flush() {
        queue.sync { [self] in
            if !buffer.isEmpty { emit(buffer); buffer.removeAll() }
        }
    }

    private func emit(_ data: Data) {
        guard let s = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespaces),
              !s.isEmpty else { return }
        tail.append(s)
        if tail.count > 200 { tail.removeFirst(tail.count - 200) }
        onLine?(s)
    }
}
