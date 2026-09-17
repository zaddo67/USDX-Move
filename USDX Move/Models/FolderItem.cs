namespace USDX_Move.Models
{
    /// <summary>
    /// Represents a discovered folder with its leaf name, relative path, and full system path.
    /// </summary>
    public class FolderItem
    {
        public string Name { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;

        public override string ToString() => Name;
    }
}
