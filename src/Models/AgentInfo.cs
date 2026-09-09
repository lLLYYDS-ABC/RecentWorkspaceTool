namespace RecentWorkspaceWidget.Models
{
    public enum AgentType
    {
        OpenCode,
        ClaudeCode,
        Codex,
        VSCode,
        Explorer
    }

    public class AgentInfo
    {
        public AgentType Type { get; set; }
        public string Name { get; set; }
        public string KeyHint { get; set; }
        public string IconEmoji { get; set; }
        public bool IsInstalled { get; set; }
        public string ExecutablePath { get; set; }
        public string CommandTemplate { get; set; }
    }
}
