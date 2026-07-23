namespace SynToolkit.Services.Bcd
{
    /// <summary>
    /// Narrow seam around the built-in Windows BCD WMI provider. The service layer owns
    /// validation and verification; this interface keeps the system boundary replaceable.
    /// </summary>
    internal interface IBcdWmiProvider
    {
        object GetElementValue(string objectId, uint elementType);
        void DeleteElement(string objectId, uint elementType);
        void SetBooleanElement(string objectId, uint elementType, bool value);
        void SetIntegerElement(string objectId, uint elementType, ulong value);
    }
}
