import Foundation

enum StreamPhase: Equatable { case preview, recording, finalizing }

struct PlayerSnapshot: Equatable {
    var ended: Bool
    var ad: Bool
    var content: Bool
    var id: String
    var receivedAt: Date
}

struct StreamDecision: Equatable {
    var stopReason: String?
    var candidateStart: Date?
    var extendedAdSeconds: Int
    var countdownSeconds: Int?
    var previewShowsEnded: Bool
    var leaveUiAlone: Bool
}

/// 跟 C# `StreamEndGate.Evaluate` 同一張表（R0–R6）。
enum StreamEnd {
    static let staleSeconds = 5.0
    static let baseSeconds = 20.0
    static let extendStep = 20
    static let maxExtend = 120
    static let otherVideoReason = "播放器換成別支影片"
    static let videoEndedReason = "影片已結束"

    static func evaluate(phase: StreamPhase, anchorId: String, snapshot: PlayerSnapshot?,
                         candidateStart: Date?, extendedAdSeconds: Int, now: Date) -> StreamDecision {
        if phase == .finalizing {
            return StreamDecision(stopReason: nil, candidateStart: candidateStart,
                                  extendedAdSeconds: extendedAdSeconds, countdownSeconds: nil,
                                  previewShowsEnded: false, leaveUiAlone: true)
        }

        let fresh = snapshot.map { now.timeIntervalSince($0.receivedAt) <= staleSeconds } ?? false
        let ended = fresh && (snapshot?.ended ?? false)
        let ad = fresh && (snapshot?.ad ?? false)
        let content = fresh && (snapshot?.content ?? false)
        let id = snapshot?.id ?? ""

        if phase == .preview {
            return StreamDecision(stopReason: nil, candidateStart: nil, extendedAdSeconds: 0,
                                  countdownSeconds: nil, previewShowsEnded: ended && !ad, leaveUiAlone: false)
        }

        if !ad && !id.isEmpty && id != anchorId {
            return StreamDecision(stopReason: otherVideoReason, candidateStart: nil, extendedAdSeconds: 0,
                                  countdownSeconds: nil, previewShowsEnded: false, leaveUiAlone: false)
        }

        var candidate = candidateStart
        var extended = extendedAdSeconds

        if candidate != nil && content && (id == anchorId || id.isEmpty) {
            return StreamDecision(stopReason: nil, candidateStart: nil, extendedAdSeconds: 0,
                                  countdownSeconds: nil, previewShowsEnded: false, leaveUiAlone: false)
        }

        if candidate == nil && ended && !ad {
            candidate = now
        }

        guard let start = candidate else {
            return StreamDecision(stopReason: nil, candidateStart: nil, extendedAdSeconds: extended,
                                  countdownSeconds: nil, previewShowsEnded: false, leaveUiAlone: false)
        }

        let limit = baseSeconds + Double(extended)
        if now.timeIntervalSince(start) >= limit {
            if ad && extended < maxExtend {
                extended += extendStep
                return StreamDecision(stopReason: nil, candidateStart: start, extendedAdSeconds: extended,
                                      countdownSeconds: countdown(start: start, extended: extended, now: now),
                                      previewShowsEnded: false, leaveUiAlone: false)
            }
            return StreamDecision(stopReason: videoEndedReason, candidateStart: nil, extendedAdSeconds: 0,
                                  countdownSeconds: nil, previewShowsEnded: false, leaveUiAlone: false)
        }

        return StreamDecision(stopReason: nil, candidateStart: start, extendedAdSeconds: extended,
                              countdownSeconds: countdown(start: start, extended: extended, now: now),
                              previewShowsEnded: false, leaveUiAlone: false)
    }

    static func canBeginFromPreview(previewReady: Bool, previewShowsEnded: Bool) -> Bool {
        previewReady && !previewShowsEnded
    }

    private static func countdown(start: Date, extended: Int, now: Date) -> Int? {
        let remain = start.addingTimeInterval(baseSeconds + Double(extended)).timeIntervalSince(now)
        if remain <= 0 { return nil }
        return Int(ceil(remain))
    }
}
