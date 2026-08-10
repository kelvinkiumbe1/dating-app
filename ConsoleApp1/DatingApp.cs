namespace ConsoleMatch;

public class DatingApp
{
    private const int CardWidth = 46;

    private readonly AppState _state;
    private readonly IReplyEngine _replies;

    public static readonly Candidate[] Pool =
    {
        new() { Name = "Sam", Age = 29, Bio = "Coffee snob, trail runner, will judge your semicolon usage.", Interests = new[] { "running", "coffee", "coding" }, Standards = 20 },
        new() { Name = "Riley", Age = 26, Bio = "Amateur chef. My risotto has a 5-star rating (from my mom).", Interests = new[] { "cooking", "board games", "travel" }, Standards = 30 },
        new() { Name = "Jordan", Age = 31, Bio = "Climber and cat parent. The cat swipes for me.", Interests = new[] { "climbing", "cats", "sci-fi" }, Standards = 15 },
        new() { Name = "Alex", Age = 27, Bio = "Vinyl collector. Yes, it sounds warmer. No, I won't stop saying that.", Interests = new[] { "music", "cycling", "film" }, Standards = 25 },
        new() { Name = "Casey", Age = 33, Bio = "Marathon watcher (of shows, not runners).", Interests = new[] { "tv", "baking", "dogs" }, Standards = 10 },
        new() { Name = "Morgan", Age = 30, Bio = "Weekend hiker, weekday debugger.", Interests = new[] { "hiking", "coding", "coffee" }, Standards = 20 },
        new() { Name = "Taylor", Age = 28, Bio = "Bookshop lurker. Ask me about my TBR pile (don't).", Interests = new[] { "books", "tea", "museums" }, Standards = 35 },
        new() { Name = "Quinn", Age = 25, Bio = "Plays bass in a band you haven't heard of. Neither has anyone.", Interests = new[] { "music", "gaming", "pizza" }, Standards = 5 },
        new() { Name = "Avery", Age = 34, Bio = "Gym at 6am, regrets at 6:05am.", Interests = new[] { "fitness", "cooking", "podcasts" }, Standards = 30 },
        new() { Name = "Reese", Age = 27, Bio = "Photographer. Will make you stand in front of walls.", Interests = new[] { "photography", "travel", "film" }, Standards = 25 },
    };

    public DatingApp(AppState? state = null, IReplyEngine? replies = null)
    {
        _state = state ?? Storage.Load();
        _replies = replies ?? (ClaudeReplyEngine.IsConfigured
            ? new ClaudeReplyEngine()
            : new CannedReplyEngine());
    }

    public async Task Run()
    {
        Header();
        if (_state.User is null) Onboard();

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"── Menu ───────────────  ({Remaining().Count} to swipe, {_state.Matches.Count} matches)");
            Console.WriteLine("  1) Swipe   2) Matches & chat   3) My profile   4) Edit profile   5) Reset   q) Quit");
            var choice = Prompt("> ", new[] { "1", "2", "3", "4", "5", "q" });

            switch (choice)
            {
                case "1": Swipe(); break;
                case "2": await Matches(); break;
                case "3": ShowMe(); break;
                case "4": EditProfile(); break;
                case "5": Reset(); return;
                case "q":
                    Storage.Save(_state);
                    Console.WriteLine("Saved. See you next run. 💘");
                    return;
            }
        }
    }

    private void Header()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("=== 💘 ConsoleMatch v3 — dating at 80 columns ===");
        Console.ResetColor();
        Console.WriteLine(_replies is ClaudeReplyEngine
            ? "Chat replies: powered by Claude ✨"
            : "Chat replies: offline mode (set ANTHROPIC_API_KEY for live replies)");
    }

    private void Onboard()
    {
        Console.WriteLine("\nNew here? Let's set up your profile.");
        _state.User = new UserProfile();
        AskProfile(_state.User);
        Storage.Save(_state);
        Console.WriteLine($"\nWelcome, {_state.User.Name}! Shared interests drive your match odds — choose wisely.");
    }

    private void EditProfile()
    {
        var u = _state.User!;
        Console.WriteLine("\nEdit your profile (press Enter to keep the current value).");
        AskProfile(u, keepExisting: true);
        Storage.Save(_state);
        Console.WriteLine("Profile updated — everyone you haven't swiped yet is re-scored.");
    }

    private static void AskProfile(UserProfile user, bool keepExisting = false)
    {
        Console.Write(keepExisting ? $"Name [{user.Name}]: " : "Your name: ");
        user.Name = ReadLineOr(keepExisting ? user.Name : "Anonymous");

        Console.Write(keepExisting ? $"Age [{user.Age}]: " : "Your age: ");
        var ageText = ReadLineOr(keepExisting ? user.Age.ToString() : "18");
        user.Age = int.TryParse(ageText, out var age) ? Math.Clamp(age, 18, 120) : Math.Max(user.Age, 18);

        Console.Write(keepExisting ? $"Bio [{user.Bio}]: " : "A short bio: ");
        user.Bio = ReadLineOr(keepExisting ? user.Bio : "Mysteriously left blank.");

        var current = string.Join(", ", user.Interests);
        Console.Write(keepExisting
            ? $"Interests [{current}]: "
            : "Your interests (comma-separated, e.g. coffee, hiking, film): ");
        user.Interests = ParseInterests(ReadLineOr(current));
    }

    public static List<string> ParseInterests(string input) =>
        input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(i => i.ToLowerInvariant())
             .Distinct()
             .ToList();

    private List<Candidate> Remaining() =>
        Pool.Where(c => !_state.Swiped.Contains(c.Name)).ToList();

    private void Swipe()
    {
        var remaining = Remaining();
        if (remaining.Count == 0)
        {
            Console.WriteLine("\nYou've swiped through everyone. Touch grass, then Reset. 🌱");
            return;
        }

        foreach (var candidate in remaining)
        {
            var compat = candidate.CompatibilityWith(_state.User!);
            ShowCard(candidate, compat);

            var choice = Prompt("Swipe [r]ight, [l]eft, or [b]ack to menu: ", new[] { "r", "l", "b" });
            if (choice == "b") return;

            _state.Swiped.Add(candidate.Name);

            if (choice == "r")
            {
                if (compat >= candidate.Standards)
                {
                    _state.Matches.Add(candidate.Name);
                    _state.Chats[candidate.Name] = new List<ChatMessage>();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n🎉 It's a match with {candidate.Name}! ({compat}% compatible)");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine($"\nYou liked {candidate.Name}, but they're holding out for {candidate.Standards}%+ compatibility. Ouch.");
                }
            }
            else
            {
                Console.WriteLine($"\nPassed on {candidate.Name}.");
            }
            Storage.Save(_state);
        }
    }

    private void ShowCard(Candidate c, int compat)
    {
        var interested = compat >= c.Standards;
        Console.WriteLine();
        Console.WriteLine("┌" + new string('─', CardWidth) + "┐");
        WriteHeaderRow($"{c.Name}, {c.Age}", Badge(compat, interested), ColorFor(compat));
        foreach (var line in Wrap(c.Bio, CardWidth - 2)) WriteRow(line);
        foreach (var line in Wrap("Into: " + string.Join(", ", c.Interests), CardWidth - 2)) WriteRow(line);
        Console.WriteLine("└" + new string('─', CardWidth) + "┘");
    }

    private static ConsoleColor ColorFor(int compat) => compat switch
    {
        >= 50 => ConsoleColor.Green,
        >= 20 => ConsoleColor.Yellow,
        _ => ConsoleColor.Red,
    };

    // Badges stay single-width ASCII/★ so the border math below stays exact —
    // emoji occupy two terminal columns but count as one char, which breaks padding.
    public static string Badge(int compat, bool interested) =>
        interested ? $"{compat}% match ★ interested" : $"{compat}% match";

    private static void WriteRow(string text) =>
        Console.WriteLine("│ " + Fit(text, CardWidth - 2) + " │");

    /// Left-aligned name with the match badge flushed right, colored, border intact.
    private static void WriteHeaderRow(string left, string right, ConsoleColor rightColor)
    {
        var inner = CardWidth - 2;
        left = Fit(left, Math.Max(0, inner - right.Length - 1));
        var gap = inner - left.Length - right.Length;

        Console.Write("│ " + left + new string(' ', Math.Max(1, gap)));
        Console.ForegroundColor = rightColor;
        Console.Write(right);
        Console.ResetColor();
        Console.WriteLine(" │");
    }

    private static string Fit(string text, int width) =>
        text.Length > width ? text[..width] : text.PadRight(width);

    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length == 0) line = word;
            else if (line.Length + 1 + word.Length <= width) line += " " + word;
            else { lines.Add(line); line = word; }
        }
        if (line.Length > 0) lines.Add(line);
        return lines.Count == 0 ? new List<string> { "" } : lines;
    }

    private async Task Matches()
    {
        if (_state.Matches.Count == 0)
        {
            Console.WriteLine("\nNo matches yet. Go swipe! 💔");
            return;
        }

        Console.WriteLine("\nYour matches:");
        for (var i = 0; i < _state.Matches.Count; i++)
        {
            var name = _state.Matches[i];
            var msgs = _state.Chats.GetValueOrDefault(name)?.Count ?? 0;
            Console.WriteLine($"  {i + 1}) {name} ({msgs} messages)");
        }

        Console.Write("Chat with # , or 'u <#>' to unmatch (Enter to go back): ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input)) return;

        if (input.StartsWith("u ", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(input[2..].Trim(), out var target)
            && target >= 1 && target <= _state.Matches.Count)
        {
            Unmatch(_state.Matches[target - 1]);
            return;
        }

        if (int.TryParse(input, out var n) && n >= 1 && n <= _state.Matches.Count)
            await Chat(_state.Matches[n - 1]);
    }

    private void Unmatch(string name)
    {
        _state.Matches.Remove(name);
        _state.Chats.Remove(name);
        Storage.Save(_state);
        Console.WriteLine($"Unmatched {name}. Their chat history is gone too.");
    }

    private async Task Chat(string name)
    {
        var candidate = Pool.First(c => c.Name == name);
        var history = _state.Chats.TryGetValue(name, out var h) ? h : _state.Chats[name] = new List<ChatMessage>();

        Console.WriteLine($"\n── Chat with {name} ── (type a message, or /back)");
        foreach (var m in history)
            Console.WriteLine($"  {(m.From == "you" ? "You" : name)}: {m.Text}");

        while (true)
        {
            Console.Write("You: ");
            var text = Console.ReadLine()?.Trim();
            if (text is null or "/back" or "") return;

            history.Add(new ChatMessage { From = "you", Text = text });

            var reply = await _replies.ReplyAsync(candidate, _state.User!, history);
            history.Add(new ChatMessage { From = name, Text = reply });
            Console.WriteLine($"  {name}: {reply}");
            Storage.Save(_state);
        }
    }

    private void ShowMe()
    {
        var u = _state.User!;
        Console.WriteLine($"\n{u.Name}, {u.Age}");
        Console.WriteLine($"  {u.Bio}");
        Console.WriteLine($"  Into: {(u.Interests.Count > 0 ? string.Join(", ", u.Interests) : "…nothing? Bold move.")}");
        Console.WriteLine($"  Stats: {_state.Swiped.Count} swiped, {_state.Matches.Count} matched");
    }

    private void Reset()
    {
        Storage.Reset();
        Console.WriteLine("Profile, swipes, and chats wiped. Fresh start on next run. 🌱");
    }

    private static string ReadLineOr(string fallback)
    {
        var input = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(input) ? fallback : input;
    }

    private static string Prompt(string message, string[] allowed)
    {
        while (true)
        {
            Console.Write(message);
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (input is null) return allowed[^1]; // no stdin — take the exit option
            if (allowed.Contains(input)) return input;
            Console.WriteLine($"Please enter one of: {string.Join(", ", allowed)}");
        }
    }
}
