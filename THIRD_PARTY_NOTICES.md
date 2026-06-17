# Third-Party Notices

This file lists third-party components used by Inari-Kontroller or included in
the public release package.

Inari-Kontroller source code is released under the MIT License. The public ZIP
also includes third-party runtime, SDK, and library components. Those
components remain under their own license terms.

## Included Components

| Component | Version | Used as | License / terms | Package status |
| --- | --- | --- | --- | --- |
| OpenVR SDK | 2.5.1 | OpenVR headers, import library, and `openvr_api.dll` | BSD 3-Clause | `openvr_api.dll` is included in the app package. License text is included in `licenses/OpenVR-LICENSE.txt`. |
| Microsoft Windows App SDK | 1.6.241114003 | Self-contained WinUI runtime for the desktop GUI | Microsoft Windows App SDK software license terms | Runtime files are included in the app package. License and notice files are included in `licenses/`. |
| Microsoft Graphics Win2D | 1.3.0 | WinUI drawing controls | Microsoft Win2D EULA, http://www.microsoft.com/web/webpi/eula/eula_win2d_10012014.htm | Runtime files may be included through publish output. |
| Microsoft.Web.WebView2 | 1.0.2651.64 | Transitive Windows App SDK dependency | Microsoft WebView2 package license | Runtime files may be included through publish output. License and notice files are included in `licenses/`. |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.1 | Build-time SDK tools | Windows SDK license | Build-time only; not intentionally included in the app package. |
| System.Security.Permissions | 8.0.0 | .NET package reference, private assets | MIT | License text is included in `licenses/DotNet-MIT-LICENSE.txt`. |
| System.Windows.Extensions | 8.0.0 | Transitive .NET dependency | MIT | Covered by the .NET MIT license text in `licenses/DotNet-MIT-LICENSE.txt`. |
| Microsoft .NET Runtime | 8.0 self-contained publish output | Runtime files bundled by `dotnet publish --self-contained` | .NET on Windows license information | License information is included in `licenses/dotnet-license-information-windows.md`. |

## Notice Text

This product includes the OpenVR SDK, Copyright (c) Valve Corporation,
licensed under the BSD 3-Clause License. A copy of the BSD 3-Clause License is
included in `licenses/OpenVR-LICENSE.txt`.

This distribution includes Microsoft runtime and framework components such as
Windows App SDK, Win2D, WebView2, and .NET runtime. These components are
redistributed under their respective Microsoft license terms. See the files in
the `licenses/` directory for details.

This product may include .NET library packages distributed under the MIT
License, including System.Security.Permissions and System.Windows.Extensions.
The applicable MIT license text is included in `licenses/DotNet-MIT-LICENSE.txt`.

## Not Included

The public source tree does not vendor nlohmann/json. If it is added in the
future, its MIT license notice should be added here and to the release package.
