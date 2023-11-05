// conversion from C++ to C#: https://learn.microsoft.com/en-us/windows/win32/fileio/working-with-nvme-devices
// there is a bug in Microsoft's example, check Example.cpp.txt for more info
// uses CsWin32: https://github.com/microsoft/CsWin32
// error codes: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/18d8fbe8-a967-4f1c-ae50-99ca8e491d2d
// get DeviceID, pwsh: Get-CimInstance -Query 'Select * from Win32_DiskDrive'
// use BitConverter for conversion of raw data
// run your whole IDE as ADMIN if you have problems, btw this is written in VSCode using .NET 8.0

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.Storage.Nvme;
using Windows.Win32.System.Ioctl;

namespace NVMe;

internal static class SMART
{
    [SupportedOSPlatform("Windows5.1.2600")]
    public static bool GetSMART(out NVME_HEALTH_INFO_LOG log, int DiskIndex = 0)
    {
        log = default;
        string drv = @"\\.\PHYSICALDRIVE" + DiskIndex; // DeviceID

        using SafeFileHandle sfh = PInvoke.CreateFile(drv, 0, FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                                                        null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, 0, null);
                                                        // DesiredAccess = 0, no need for ADMIN, maybe?!

        if (sfh.IsInvalid == true)
        {
            string err = Marshal.GetLastWin32Error().ToString("X8").Insert(0, "0x");
            Console.WriteLine("CreateFile failed"); Console.WriteLine("Win32ErrorCode: " + err);
            return false;
        }

        BOOL result;

        unsafe
        {
            void* buffer = null;
            ulong bufferLength = 0;
            ulong returnedLength = 0;

            STORAGE_PROPERTY_QUERY* query = null;
            STORAGE_PROTOCOL_SPECIFIC_DATA* protocolData = null;
            STORAGE_PROTOCOL_DATA_DESCRIPTOR* protocolDataDescr = null;

            bufferLength = (ulong)Marshal.OffsetOf<STORAGE_PROPERTY_QUERY>("AdditionalParameters");
            bufferLength += (ulong)sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA);
            bufferLength += PInvoke.NVME_MAX_LOG_SIZE; // maybe sizeof(NVME_HEALTH_INFO_LOG) is enough ?!

            try
            {
                buffer = (void*)Marshal.AllocHGlobal((int)bufferLength);
            }
            catch (Exception ex)
            {
                Console.WriteLine("DeviceNVMeQueryProtocolDataTest: allocate buffer failed, exit.");
                Console.WriteLine(ex.Message);
                return false;
            }

            query = (STORAGE_PROPERTY_QUERY*)buffer;
            protocolDataDescr = (STORAGE_PROTOCOL_DATA_DESCRIPTOR*)buffer;
            //protocolData = (STORAGE_PROTOCOL_SPECIFIC_DATA*)query->AdditionalParameters.Value;
            protocolData = (STORAGE_PROTOCOL_SPECIFIC_DATA*)&query->AdditionalParameters;

            query->PropertyId = STORAGE_PROPERTY_ID.StorageDeviceProtocolSpecificProperty;
            query->QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery;

            protocolData->ProtocolType = STORAGE_PROTOCOL_TYPE.ProtocolTypeNvme;
            protocolData->DataType = (uint)STORAGE_PROTOCOL_NVME_DATA_TYPE.NVMeDataTypeLogPage;
            protocolData->ProtocolDataRequestValue = (uint)NVME_LOG_PAGES.NVME_LOG_PAGE_HEALTH_INFO;
            protocolData->ProtocolDataRequestSubValue = 0; protocolData->ProtocolDataRequestSubValue2 = 0;
            protocolData->ProtocolDataRequestSubValue3 = 0; protocolData->ProtocolDataRequestSubValue4 = 0;
            protocolData->ProtocolDataOffset = (uint)sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA);
            protocolData->ProtocolDataLength = (uint)sizeof(NVME_HEALTH_INFO_LOG);

            result = PInvoke.DeviceIoControl(sfh, PInvoke.IOCTL_STORAGE_QUERY_PROPERTY, buffer, (uint)bufferLength,
                                                buffer, (uint)bufferLength, (uint*)&returnedLength, null);

            bool OK = true;

            if (result == false || returnedLength == 0)
            {
                string err = Marshal.GetLastWin32Error().ToString("X8").Insert(0, "0x");
                Console.WriteLine("DeviceNVMeQueryProtocolDataTest: SMART/Health Information Log failed.");
                Console.WriteLine("DeviceIoControl failed"); Console.WriteLine("Win32ErrorCode: " + err);
                OK = false; goto EXIT;
            }

            if ((protocolDataDescr->Version != sizeof(STORAGE_PROTOCOL_DATA_DESCRIPTOR)) ||
                (protocolDataDescr->Size != sizeof(STORAGE_PROTOCOL_DATA_DESCRIPTOR)))
            {
                Console.WriteLine("DeviceNVMeQueryProtocolDataTest: SMART/Health Information Log - data descriptor header not valid.");
                OK = false; goto EXIT;
            }

            protocolData = &protocolDataDescr->ProtocolSpecificData;

            if ((protocolData->ProtocolDataOffset < sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA)) ||
                (protocolData->ProtocolDataLength < sizeof(NVME_HEALTH_INFO_LOG)))
            {
                Console.WriteLine("DeviceNVMeQueryProtocolDataTest: SMART/Health Information Log - ProtocolData Offset/Length not valid.");
                OK = false; goto EXIT;
            }

            NVME_HEALTH_INFO_LOG* hil = (NVME_HEALTH_INFO_LOG*)((byte*)protocolData + protocolData->ProtocolDataOffset);
            log = Marshal.PtrToStructure<NVME_HEALTH_INFO_LOG>((IntPtr)hil);

        EXIT:
            Marshal.FreeHGlobal((IntPtr)buffer);
            return OK;
        }
    }
}
