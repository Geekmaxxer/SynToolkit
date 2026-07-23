namespace SynToolkit.Services
{
    public interface IBcdService
    {
        void DeleteElement(string objectId, uint elementType);
        object GetElementValue(string objectId, uint elementType);
        void SetBooleanElement(string objectId, uint elementType, bool value);
        void SetIntegerElement(string objectId, uint elementType, ulong value);
    }
}
