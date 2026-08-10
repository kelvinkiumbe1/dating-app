# dating-app

**ConsoleMatch** — a dating app that lives in your terminal. Swipe, match, and chat with
profiles whose replies are written by Claude.

## Quick start

```bash
dotnet run --project ConsoleApp1
```

First run walks you through a profile (name, age, bio, interests). Everything after that is
menu-driven, and your progress is saved between runs.

## How matching works

Compatibility is the [Jaccard index](https://en.wikipedia.org/wiki/Jaccard_index) of your
interests against a candidate's — shared interests divided by the union of both sets. Each
candidate also has their own `Standards` threshold, so a right-swipe becomes a match only when
your compatibility meets or beats their bar. A `★ interested` badge on a card means you
already clear it. The badge is color-coded: green at 50%+, yellow at 20%+, red below that.

## Live chat replies

Chat replies come from Claude when an API key is present:

```bash
export ANTHROPIC_API_KEY=sk-ant-...      # PowerShell: $env:ANTHROPIC_API_KEY = "sk-ant-..."
dotnet run --project ConsoleApp1
```

Each match gets a system prompt built from both profiles and the compatibility score, so replies
stay in character. Without a key — or if the network drops mid-conversation — the app falls back
to canned lines instead of failing, and the header tells you which mode you're in.

## Project layout

| Path | What's in it |
| --- | --- |
| `ConsoleApp1/Models.cs` | `UserProfile`, `Candidate` (compatibility scoring), `ChatMessage`, `AppState` |
| `ConsoleApp1/DatingApp.cs` | Menu loop, swipe deck, card rendering, chat |
| `ConsoleApp1/ReplyEngine.cs` | Claude-backed replies with an offline fallback |
| `ConsoleApp1/Storage.cs` | JSON save/load of profile, swipes, matches, and chats |
| `ConsoleApp1.Tests/` | xUnit coverage for scoring, persistence, parsing, and wrapping |

## Tests

```bash
dotnet test
```

CI runs the same build and test suite on every push and pull request to `main`.
