# Third-Party Notices

This file lists third-party components used by VR Stick Scope or included in
the public release package.

| Component | Version | Used as | License source checked | Redistribution status |
| --- | --- | --- | --- | --- |
| OpenVR SDK | 2.5.1 | OpenVR headers, import library, and `openvr_api.dll` | BSD 3-Clause, https://github.com/ValveSoftware/openvr/blob/master/LICENSE | Included in the app package. |
| Microsoft Windows App SDK | 1.6.241114003 | Self-contained WinUI runtime for the desktop GUI | `microsoft.windowsappsdk.nuspec` points to `license.txt`; Microsoft Windows App SDK software license terms | Runtime files included in the app package. |
| Microsoft Graphics Win2D | 1.3.0 | WinUI drawing controls | `microsoft.graphics.win2d.nuspec` points to Microsoft Win2D EULA, http://www.microsoft.com/web/webpi/eula/eula_win2d_10012014.htm | Runtime files included in the app package. |
| Microsoft.Web.WebView2 | 1.0.2651.64 | Transitive Windows App SDK dependency | `microsoft.web.webview2.nuspec` points to `LICENSE.txt`; BSD-style Microsoft license | Runtime files included in the app package. |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.1 | Build-time SDK tools | `microsoft.windows.sdk.buildtools.nuspec` points to Windows SDK license, https://aka.ms/WinSDKLicenseURL | Build-time only; not intentionally included in the app package. |
| System.Security.Permissions | 8.0.0 | .NET package reference, private assets | MIT, https://licenses.nuget.org/MIT | Not intentionally included as a separate package file; covered by .NET publish output if copied transitively. |
| System.Windows.Extensions | 8.0.0 | Transitive .NET dependency | MIT, https://licenses.nuget.org/MIT | Runtime files may be included by self-contained publish. |
| Microsoft .NET Runtime | 8.0 self-contained publish output | Runtime files bundled by `dotnet publish --self-contained` | .NET on Windows license information, https://github.com/dotnet/core/blob/main/license-information-windows.md | Runtime files included in the app package. |
