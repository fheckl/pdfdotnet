namespace pdfdotnet;

/// <summary>
/// Options for a digital signature applied to a PDF document.
/// </summary>
public sealed class PdfSignatureOptions
{
    /// <summary>Reason for signing (displayed in the signature UI).</summary>
    public string? Reason { get; set; }

    /// <summary>Contact information of the signer.</summary>
    public string? ContactInfo { get; set; }

    /// <summary>Location where signing took place.</summary>
    public string? Location { get; set; }

    /// <summary>Signing date. Default: current UTC time.</summary>
    public DateTime SignDate { get; set; } = DateTime.UtcNow;

    /// <summary>Signature filter. Default: /Adobe.PPKLite.</summary>
    public string Filter { get; set; } = "/Adobe.PPKLite";

    /// <summary>Signature sub-filter. Default: /adbe.pkcs7.detached.</summary>
    public string SubFilter { get; set; } = "/adbe.pkcs7.detached";

    /// <summary>
    /// Expected length (in bytes) of the raw signature value produced by <see cref="Sign"/>.
    /// The signature Contents is written as a fixed-length placeholder, so the actual
    /// signature must match this length. Default: 256.
    /// </summary>
    public int PlaceholderLength { get; set; } = 256;

    /// <summary>
    /// Name of the signer.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Signs the given byte range data and returns the signature value bytes (Contents).
    /// The delegate receives the concatenated byte-range data that must be signed and
    /// returns the raw PKCS#7/CMS signature bytes (or any bytes the chosen SubFilter expects).
    /// </summary>
    public Func<byte[], byte[]>? Sign { get; set; }
}
