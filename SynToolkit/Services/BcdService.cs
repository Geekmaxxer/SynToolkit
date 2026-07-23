using SynToolkit.Services.Bcd;
using System;

namespace SynToolkit.Services
{
    /// <summary>
    /// Validates and verifies changes made through Windows' built-in BCD WMI provider.
    /// </summary>
    internal sealed class BcdService : IBcdService
    {
        private readonly IBcdWmiProvider _provider;

        public BcdService(IBcdWmiProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public void DeleteElement(string objectId, uint elementType)
        {
            string normalizedObjectId = BcdContract.NormalizeObjectIdentifier(objectId);
            _ = BcdContract.GetValueKind(elementType);

            if (_provider.GetElementValue(normalizedObjectId, elementType) is null)
            {
                App.logger.Info(
                    $"[BCD] Element 0x{elementType:X8} is already absent from object {normalizedObjectId}");
                return;
            }

            _provider.DeleteElement(normalizedObjectId, elementType);
            if (_provider.GetElementValue(normalizedObjectId, elementType) is not null)
            {
                throw new InvalidOperationException(
                    $"BCD element 0x{elementType:X8} on object {normalizedObjectId} was not deleted.");
            }

            App.logger.Info(
                $"[BCD] Deleted element 0x{elementType:X8} from object {normalizedObjectId}");
        }

        public object GetElementValue(string objectId, uint elementType)
        {
            string normalizedObjectId = BcdContract.NormalizeObjectIdentifier(objectId);
            _ = BcdContract.GetValueKind(elementType);
            return _provider.GetElementValue(normalizedObjectId, elementType);
        }

        public void SetBooleanElement(string objectId, uint elementType, bool value)
        {
            string normalizedObjectId = BcdContract.NormalizeObjectIdentifier(objectId);
            EnsureValueKind(elementType, BcdElementValueKind.Boolean);
            _provider.SetBooleanElement(normalizedObjectId, elementType, value);

            if (_provider.GetElementValue(normalizedObjectId, elementType) is not bool actual
                || actual != value)
            {
                throw new InvalidOperationException(
                    $"BCD boolean element 0x{elementType:X8} on object {normalizedObjectId} " +
                    "did not retain the requested value.");
            }
        }

        public void SetIntegerElement(string objectId, uint elementType, ulong value)
        {
            string normalizedObjectId = BcdContract.NormalizeObjectIdentifier(objectId);
            EnsureValueKind(elementType, BcdElementValueKind.Integer);
            _provider.SetIntegerElement(normalizedObjectId, elementType, value);

            object actual = _provider.GetElementValue(normalizedObjectId, elementType);
            if (!BcdContract.TryConvertToUInt64(actual, out ulong actualValue) || actualValue != value)
            {
                throw new InvalidOperationException(
                    $"BCD integer element 0x{elementType:X8} on object {normalizedObjectId} " +
                    "did not retain the requested value.");
            }
        }

        private static void EnsureValueKind(uint elementType, BcdElementValueKind expected)
        {
            BcdElementValueKind actual = BcdContract.GetValueKind(elementType);
            if (actual != expected)
            {
                throw new ArgumentException(
                    $"BCD element type 0x{elementType:X8} is {actual}, not {expected}.",
                    nameof(elementType));
            }
        }
    }
}
