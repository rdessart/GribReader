namespace ESky.Grib;

public class GribException : Exception
{
    public GribException(string message) : base(message) { }
    public GribException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class UnsupportedGribTemplateException : GribException
{
    public UnsupportedGribTemplateException(string section, ushort templateNumber)
        : base($"Unsupported GRIB2 {section} template {templateNumber}.")
    {
        Section = section;
        TemplateNumber = templateNumber;
    }

    public string Section { get; }
    public ushort TemplateNumber { get; }
}
