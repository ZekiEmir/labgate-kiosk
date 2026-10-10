namespace Kiosk.Web.Models;

public sealed class User
{
    public int Id { get; set; }
    public string OgrenciNo { get; set; } = "";
    public string AdSoyad { get; set; } = "";
    public string Kart_UID { get; set; } = "";
    public byte[]? Face_Encoding { get; set; }
    public bool Yetki_Durumu { get; set; }
}

public sealed class AccessLog
{
    public int LogId { get; set; }
    // Unknown cards have no user; their denied attempts still remain auditable.
    public int? UserId { get; set; }
    public User? User { get; set; }
    public DateTime Giris_Zamani { get; set; }
    public string Durum { get; set; } = "";
    public string Kart_UID { get; set; } = "";
    public bool IsSimulation { get; set; }
    public bool AccessGranted { get; set; }
}
