# Third-party notices

Smart Paste is released under the [MIT License](LICENSE). It ships with the open-source components below. Each one
keeps its own license, which is reproduced or linked here. The licenses were checked against each package's own
metadata and license files.

## What ships

| Component | Version | License | Project |
| --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | [github.com/CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet) |
| H.NotifyIcon.Wpf | 2.4.1 | MIT | [github.com/HavenDV/H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) |
| H.NotifyIcon | 2.4.1 | MIT | [github.com/HavenDV/H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) |
| H.GeneratedIcons.System.Drawing | 2.4.1 | MIT | [github.com/HavenDV/H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) |
| Inter typeface (Inter and Inter Display, fixed-weight files made from the variable font) | 4.001 | SIL Open Font License 1.1 | [github.com/rsms/inter](https://github.com/rsms/inter) |
| Interop.UIAutomationClient | 10.19041.0 | MIT | [github.com/Roemer/UIAutomation-Interop](https://github.com/Roemer/UIAutomation-Interop) |
| Velopack | 1.2.161 | MIT | [github.com/velopack/velopack](https://github.com/velopack/velopack) |
| Velopack installer and updater (Setup.exe, Update.exe, and the `Smart Paste.exe` launcher), from the vpk package | 1.2.161 | MIT | [github.com/velopack/velopack](https://github.com/velopack/velopack) |
| WPF-UI | 4.3.0 | MIT | [github.com/lepoco/wpfui](https://github.com/lepoco/wpfui) |
| WPF-UI.Abstractions | 4.3.0 | MIT | [github.com/lepoco/wpfui](https://github.com/lepoco/wpfui) |
| Microsoft.Windows.SDK.NET.Ref (`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`) | 10.0.19041.57 | Microsoft Windows SDK license | [nuget.org/packages/Microsoft.Windows.SDK.NET.Ref](https://www.nuget.org/packages/Microsoft.Windows.SDK.NET.Ref) |
| .NET app host (`SmartPaste.exe`), from Microsoft.NETCore.App.Host.win-x64 and win-arm64 | The .NET 10 SDK's version at build time | MIT | [github.com/dotnet/runtime](https://github.com/dotnet/runtime) |

The .NET 10 Desktop Runtime does not ship with Smart Paste. When it is missing, the installer downloads it from
Microsoft.

## CommunityToolkit.Mvvm 8.4.2

Copyright © .NET Foundation and Contributors. All rights reserved.

MIT License (MIT)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the “Software”), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NON-INFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

The toolkit's own third-party notices are in its
[ThirdPartyNotices.txt](https://github.com/CommunityToolkit/dotnet/blob/35dfe7b12da0e64384bfaec7ebc0883196a89f9f/ThirdPartyNotices.txt),
which also ships inside the package.

## H.NotifyIcon.Wpf, H.NotifyIcon, and H.GeneratedIcons.System.Drawing 2.4.1

MIT License

Copyright (c) 2020 havendv

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Source of this text: [LICENSE.md](https://github.com/HavenDV/H.NotifyIcon/blob/bcaaabecf16566dd910a3798fe874014e251b28d/LICENSE.md)
at the commit the 2.4.1 packages were built from.

## Inter 4.001

Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter)

Smart Paste bundles fixed-weight files (Regular, Medium, SemiBold, Bold, and Inter Display SemiBold and Bold) made
from Inter's variable font, `InterVariable.ttf`, with fontTools' instancer. They are licensed under the SIL Open Font
License, Version 1.1, like the original. The full license text ships next to the fonts in
[src/SmartPaste.App/Assets/Fonts/Inter-LICENSE.txt](src/SmartPaste.App/Assets/Fonts/Inter-LICENSE.txt) and is also
available with a FAQ at [openfontlicense.org](https://openfontlicense.org).

## Interop.UIAutomationClient 10.19041.0

MIT License

Copyright (c) 2019 Roman

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Source of this text: `LICENSE.txt` inside the package.

## Velopack 1.2.161, and the Velopack installer and updater from vpk 1.2.161

Copyright © 2021 Caelan Sayler
Copyright © 2024 Velopack Ltd.

Permission is hereby granted,  free of charge,  to any person obtaining a
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to  use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.

Source of this text: [LICENSE](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/LICENSE)
at the commit version 1.2.161 was built from.

Setup.exe, Update.exe, and the `Smart Paste.exe` launcher are Velopack's prebuilt programs, written in Rust. They
include open-source Rust libraries under permissive licenses; Velopack lists the licenses it accepts in its
[deny.toml](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/deny.toml).

## WPF-UI and WPF-UI.Abstractions 4.3.0

MIT License

Copyright (c) 2021-2025 Leszek Pomianowski and WPF UI Contributors. https://lepo.co/

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Source of this text: `LICENSE.md` inside both packages.

WPF-UI includes parts of other projects, among them the Fluent System Icons font from Microsoft, which is embedded
in `Wpf.Ui.dll`. Their notices are in WPF-UI's
[ThirdPartyNotices.txt](https://github.com/lepoco/wpfui/blob/153baadeaf475875a81a4605881ca5c2dbe53972/ThirdPartyNotices.txt),
which also ships inside the package.

## Microsoft.Windows.SDK.NET.Ref 10.0.19041.57

`Microsoft.Windows.SDK.NET.dll` (the .NET projection of the Windows APIs, used for the built-in text recognition)
and `WinRT.Runtime.dll` come from this package.

© Microsoft Corporation. All rights reserved.

License: the Microsoft Windows SDK license terms, at [aka.ms/WinSDKLicenseURL](https://aka.ms/WinSDKLicenseURL)
(the link named in the package's metadata).

## .NET app host

`SmartPaste.exe` is the .NET app host, a small launcher that starts `SmartPaste.dll` on the installed .NET runtime.
It comes from the Microsoft.NETCore.App.Host.win-x64 and Microsoft.NETCore.App.Host.win-arm64 packages.

Copyright (c) .NET Foundation and Contributors

License: MIT, at [LICENSE.TXT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT). The .NET runtime's own
third-party notices are in [THIRD-PARTY-NOTICES.TXT](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT).
