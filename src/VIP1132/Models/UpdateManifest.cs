namespace VIP1132.Models;

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public string InstallerUrl { get; set; } = "";
    public string PortableUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
