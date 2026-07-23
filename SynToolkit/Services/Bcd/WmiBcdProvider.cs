using System;
using System.Management;

namespace SynToolkit.Services.Bcd
{
    /// <summary>
    /// Typed adapter for Microsoft's built-in Root\WMI BCD provider. It deliberately does
    /// not invoke or parse BCDEdit, whose display output can be localized.
    /// </summary>
    internal sealed class WmiBcdProvider : IBcdWmiProvider
    {
        private const string BcdNamespace = @"\\.\root\WMI";

        public object GetElementValue(string objectId, uint elementType)
        {
            BcdElementValueKind kind = BcdContract.GetValueKind(elementType);
            using ManagementObject bcdObject = OpenObject(objectId);
            using ManagementBaseObject input = bcdObject.GetMethodParameters("GetElement");
            input["Type"] = elementType;
            using ManagementBaseObject output = Invoke(bcdObject, "GetElement", input);

            if (!ReturnedSuccess(output))
            {
                return null;
            }

            using ManagementBaseObject element = output["Element"] as ManagementBaseObject;
            if (element is null)
            {
                throw new InvalidOperationException(
                    $"Windows returned no value for BCD element 0x{elementType:X8}.");
            }

            return kind switch
            {
                BcdElementValueKind.Boolean => ReadRequiredValue<bool>(element, "Boolean"),
                BcdElementValueKind.Integer => ReadRequiredInteger(element, elementType),
                _ => throw new NotSupportedException(
                    $"BCD element type 0x{elementType:X8} is not supported by SynToolkit.")
            };
        }

        public void DeleteElement(string objectId, uint elementType)
        {
            _ = BcdContract.GetValueKind(elementType);
            using ManagementObject bcdObject = OpenObject(objectId);
            using ManagementBaseObject input = bcdObject.GetMethodParameters("DeleteElement");
            input["Type"] = elementType;
            using ManagementBaseObject output = Invoke(bcdObject, "DeleteElement", input);
            EnsureSuccess(output, "delete", objectId, elementType);
        }

        public void SetBooleanElement(string objectId, uint elementType, bool value)
        {
            EnsureValueKind(elementType, BcdElementValueKind.Boolean);
            using ManagementObject bcdObject = OpenObject(objectId);
            using ManagementBaseObject input = bcdObject.GetMethodParameters("SetBooleanElement");
            input["Type"] = elementType;
            input["Boolean"] = value;
            using ManagementBaseObject output = Invoke(bcdObject, "SetBooleanElement", input);
            EnsureSuccess(output, "set", objectId, elementType);
        }

        public void SetIntegerElement(string objectId, uint elementType, ulong value)
        {
            EnsureValueKind(elementType, BcdElementValueKind.Integer);
            using ManagementObject bcdObject = OpenObject(objectId);
            using ManagementBaseObject input = bcdObject.GetMethodParameters("SetIntegerElement");
            input["Type"] = elementType;
            input["Integer"] = value;
            using ManagementBaseObject output = Invoke(bcdObject, "SetIntegerElement", input);
            EnsureSuccess(output, "set", objectId, elementType);
        }

        private static ManagementObject OpenObject(string objectId)
        {
            string normalizedObjectId = BcdContract.NormalizeObjectIdentifier(objectId);
            try
            {
                ManagementScope scope = CreateScope();
                using ManagementClass storeClass = new(scope, new ManagementPath("BcdStore"), null);
                using ManagementBaseObject openStoreInput = storeClass.GetMethodParameters("OpenStore");
                openStoreInput["File"] = string.Empty;
                using ManagementBaseObject openStoreOutput = Invoke(storeClass, "OpenStore", openStoreInput);
                EnsureSuccess(openStoreOutput, "open the system store");

                using ManagementBaseObject embeddedStore =
                    openStoreOutput["Store"] as ManagementBaseObject
                    ?? throw new InvalidOperationException("Windows did not return the system BCD store.");
                string storeFilePath = ReadRequiredValue<string>(embeddedStore, "FilePath");

                using ManagementObject store = CreateInstance(
                    scope,
                    "BcdStore",
                    $"FilePath=\"{BcdContract.EscapeManagementPathKey(storeFilePath)}\"");
                using ManagementBaseObject openObjectInput = store.GetMethodParameters("OpenObject");
                openObjectInput["Id"] = normalizedObjectId;
                using ManagementBaseObject openObjectOutput = Invoke(store, "OpenObject", openObjectInput);
                EnsureSuccess(openObjectOutput, "open", normalizedObjectId);

                using ManagementBaseObject embeddedObject =
                    openObjectOutput["Object"] as ManagementBaseObject
                    ?? throw new InvalidOperationException(
                        $"Windows did not return BCD object {normalizedObjectId}.");
                string resolvedObjectId = ReadRequiredValue<string>(embeddedObject, "Id");
                string resolvedStorePath = ReadRequiredValue<string>(embeddedObject, "StoreFilePath");

                return CreateInstance(
                    scope,
                    "BcdObject",
                    $"Id=\"{BcdContract.EscapeManagementPathKey(resolvedObjectId)}\"," +
                    $"StoreFilePath=\"{BcdContract.EscapeManagementPathKey(resolvedStorePath)}\"");
            }
            catch (ManagementException exception)
            {
                throw new InvalidOperationException(
                    $"Windows could not open BCD object {normalizedObjectId}. " +
                    "SynToolkit must be run as administrator and the Windows BCD WMI provider must be available.",
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new InvalidOperationException(
                    "Windows denied access to the BCD store. Run SynToolkit as administrator.",
                    exception);
            }
        }

        private static ManagementScope CreateScope()
        {
            ManagementScope scope = new(
                BcdNamespace,
                new ConnectionOptions
                {
                    EnablePrivileges = true,
                    Impersonation = ImpersonationLevel.Impersonate
                });
            scope.Connect();
            return scope;
        }

        private static ManagementObject CreateInstance(
            ManagementScope scope,
            string className,
            string keyExpression)
        {
            ManagementObject instance = new(
                scope,
                new ManagementPath($"{className}.{keyExpression}"),
                null);
            instance.Get();
            return instance;
        }

        private static ManagementBaseObject Invoke(
            ManagementObject target,
            string methodName,
            ManagementBaseObject input)
        {
            return target.InvokeMethod(methodName, input, null)
                ?? throw new InvalidOperationException(
                    $"Windows returned no result from BCD method {methodName}.");
        }

        private static ManagementBaseObject Invoke(
            ManagementClass target,
            string methodName,
            ManagementBaseObject input)
        {
            return target.InvokeMethod(methodName, input, null)
                ?? throw new InvalidOperationException(
                    $"Windows returned no result from BCD method {methodName}.");
        }

        private static bool ReturnedSuccess(ManagementBaseObject output)
        {
            object value = output["ReturnValue"];
            return value is bool succeeded && succeeded;
        }

        private static void EnsureSuccess(
            ManagementBaseObject output,
            string operation,
            string objectId = null,
            uint? elementType = null)
        {
            if (ReturnedSuccess(output))
            {
                return;
            }

            string target = objectId is null ? string.Empty : $" BCD object {objectId}";
            string element = elementType.HasValue ? $", element 0x{elementType.Value:X8}" : string.Empty;
            throw new InvalidOperationException(
                $"Windows could not {operation}{target}{element}.");
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

        private static T ReadRequiredValue<T>(ManagementBaseObject source, string propertyName)
        {
            object value = source[propertyName];
            if (value is T typedValue)
            {
                return typedValue;
            }

            throw new InvalidOperationException(
                $"Windows returned an invalid {propertyName} value from the BCD provider.");
        }

        private static ulong ReadRequiredInteger(ManagementBaseObject element, uint elementType)
        {
            object value = element["Integer"];
            if (BcdContract.TryConvertToUInt64(value, out ulong integer))
            {
                return integer;
            }

            throw new InvalidOperationException(
                $"Windows returned an invalid integer for BCD element 0x{elementType:X8}.");
        }
    }
}
