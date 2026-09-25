namespace LocalizationSystem
{
    /// <summary>Ready-made "Game Context" prompts for common genres, offered in the "AI Settings..." preset dropdown.</summary>
    internal static class GameContextPresets
    {
        public static readonly (string Name, string Prompt)[] BuiltIn =
        {
            ("Fantasy RPG", "Fantasy RPG, epic/formal tone, similar to Skyrim or The Witcher. Medieval setting with magic and monsters."),
            ("Sci-Fi / Space", "Science fiction game set in space, futuristic tone, similar to Mass Effect or Halo. Technical/military terminology where appropriate."),
            ("Horror", "Horror game, tense and unsettling tone, similar to Resident Evil or Silent Hill. Avoid overly casual phrasing."),
            ("FPS / Shooter", "Fast-paced first-person shooter, similar to Call of Duty. Punchy, action-oriented, military/tactical terminology."),
            ("Visual Novel", "Visual novel / dating sim, narrative-heavy, expressive and emotional tone, similar to Doki Doki Literature Club."),
            ("Strategy", "Turn-based or real-time strategy game, similar to Civilization or Age of Empires. Precise, informative tone for UI and menus."),
            ("Racing", "Arcade racing game, energetic and exciting tone, similar to Need for Speed."),
            ("Platformer", "2D/3D platformer, playful and lighthearted tone, similar to Super Mario or Rayman."),
            ("Simulation", "Life/management simulation game, similar to The Sims or Stardew Valley. Warm, approachable tone."),
            ("Casual / Mobile Puzzle", "Casual mobile puzzle game, light and friendly tone, similar to Candy Crush. Short, punchy UI text."),
        };
    }
}
