import Foundation

enum StreamPhase: Equatable { case preview, recording }

struct PlayerSnapshot: Equatable {
    var ended: Bool
    var ad: Bool
    var content: Bool
    var id: String
    /// Monotonic clock (seconds), not wall time.
    var receivedAt: TimeInterval
}

struct StreamDecision: Equatable {
    var stopReason: String?
    var anchorId: String
    var candidateStart: TimeInterval?
    var extendedAdSeconds: Int
    var otherVideoStreak: Int
    var countdownSeconds: Int?
    var previewShowsEnded: Bool
    var previewShowsOtherVideo: Bool
}

/// 跟 C# `StreamEndGate.Evaluate` 同一張表。收尾前先停計時器，所以沒有 Finalizing 分支。
enum StreamEnd {
    static let staleSeconds = 5.0
    static let baseSeconds = 20.0
    static let extendStep = 20
    static let maxExtend = 120
    static let otherVideoStreakNeeded = 3
    static let otherVideoReason = "播放器換成別支影片"
    static let videoEndedReason = "影片已結束"

    static func evaluate(phase: StreamPhase, anchorId: String, snapshot: PlayerSnapshot?,
                         candidateStart: TimeInterval?, extendedAdSeconds: Int, otherVideoStreak: Int,
                         now: TimeInterval) -> StreamDecision {
        var ended = false
        var ad = false
        var content = false
        var id = ""
        if let snap = snapshot, now - snap.receivedAt <= staleSeconds {
            ended = snap.ended
            ad = snap.ad
            content = snap.content
            id = snap.id
        }

        var anchor = anchorId
        if phase == .preview && anchor.isEmpty && !ad && content && !id.isEmpty {
            anchor = id
        }

        let mismatch = !anchor.isEmpty && !ad && !id.isEmpty && id != anchor
        let streak = mismatch ? otherVideoStreak + 1 : 0
        let switched = streak >= otherVideoStreakNeeded
        let showsEnded = ended && !ad

        if phase == .preview {
            return StreamDecision(stopReason: nil, anchorId: anchor, candidateStart: nil,
                                  extendedAdSeconds: 0, otherVideoStreak: streak, countdownSeconds: nil,
                                  previewShowsEnded: showsEnded, previewShowsOtherVideo: switched)
        }

        if switched {
            return StreamDecision(stopReason: otherVideoReason, anchorId: anchor, candidateStart: nil,
                                  extendedAdSeconds: 0, otherVideoStreak: streak, countdownSeconds: nil,
                                  previewShowsEnded: false, previewShowsOtherVideo: false)
        }

        var candidate = candidateStart
        var extended = extendedAdSeconds

        if candidate != nil && content && (id == anchor || id.isEmpty) {
            return StreamDecision(stopReason: nil, anchorId: anchor, candidateStart: nil,
                                  extendedAdSeconds: 0, otherVideoStreak: streak, countdownSeconds: nil,
                                  previewShowsEnded: false, previewShowsOtherVideo: false)
        }

        if candidate == nil && ended && !ad {
            candidate = now
        }

        guard let start = candidate else {
            return StreamDecision(stopReason: nil, anchorId: anchor, candidateStart: nil,
                                  extendedAdSeconds: extended, otherVideoStreak: streak, countdownSeconds: nil,
                                  previewShowsEnded: false, previewShowsOtherVideo: false)
        }

        let limit = baseSeconds + Double(extended)
        if now - start >= limit {
            if ad && extended < maxExtend {
                extended += extendStep
                return StreamDecision(stopReason: nil, anchorId: anchor, candidateStart: start,
                                      extendedAdSeconds: extended, otherVideoStreak: streak,
                                      countdownSeconds: countdown(start: start, extended: extended, now: now),
                                      previewShowsEnded: false, previewShowsOtherVideo: false)
            }
            return StreamDecision(stopReason: videoEndedReason, anchorId: anchor, candidateStart: nil,
                                  extendedAdSeconds: 0, otherVideoStreak: streak, countdownSeconds: nil,
                                  previewShowsEnded: false, previewShowsOtherVideo: false)
        }

        return StreamDecision(stopReason: nil, anchorId: anchor, candidateStart: start,
                              extendedAdSeconds: extended, otherVideoStreak: streak,
                              countdownSeconds: countdown(start: start, extended: extended, now: now),
                              previewShowsEnded: false, previewShowsOtherVideo: false)
    }

    static func canBeginFromPreview(previewReady: Bool, previewShowsEnded: Bool, previewShowsOtherVideo: Bool) -> Bool {
        previewReady && !previewShowsEnded && !previewShowsOtherVideo
    }

    /// 預覽可以放行的正片：新鮮、不是廣告、內容在播。過期或沒有快照都不算。
    static func previewContentReady(snapshot: PlayerSnapshot?, now: TimeInterval) -> Bool {
        guard let snap = snapshot, now - snap.receivedAt <= staleSeconds else { return false }
        return snap.content && !snap.ad
    }

    private static func countdown(start: TimeInterval, extended: Int, now: TimeInterval) -> Int? {
        let remain = (start + baseSeconds + Double(extended)) - now
        if remain <= 0 { return nil }
        return Int(ceil(remain))
    }
}
