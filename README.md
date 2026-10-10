# ObjectBuilderV2

This repository is based on [punkice3407/ObjectBuilder](https://github.com/punkice3407/ObjectBuilder), imported at commit `b07f254`. The upstream Git history, MIT license, source-code notices, and contributor credits are preserved. The application currently retains its original Object Builder name and behavior.

What is Object Builder?
----

Object Builder is an editor for creating graphic objects for [OTClient](https://github.com/edubart/otclient). You can edit or add new items, outfits, effects or missiles to your client.

Some features:

* Support frame groups.
* It's faster to load or compile your projects.
* Import or export your objects as sprite sheet.
* Export All objects or sprites at once.
* Support for transparent sprites.
* You're able to cut the imported images.
* Modify server attributes and flags directly.
* Optimized rendering and memory usage for large projects.
* 4K / high-DPI display support.
* Bulk replace objects and sprites.
* Quick Save and multiple window support.
* Edit Pixels - shift sprite pixels with boundary protection.
* Film Roll view for browsing animation frames.
* Pixel grid overlay for precise editing.


Supported client versions:
----

* 7.10 - 13.10


Downloads
----

#### Windows

AIR runtime downloads are available from [HARMAN](https://airsdk.harman.com/runtime).
For upstream prebuilt binaries, see the [upstream releases](https://github.com/punkice3407/ObjectBuilder/releases). This repository provides the source code and local build helper; compiled packages are not included in Git.


Compiling in Windows
----

Follow the [upstream build guide](https://github.com/punkice3407/ObjectBuilder/wiki/Compiling-Object-Builder-(Windows)) to prepare Apache Flex with the HARMAN AIR SDK. Accept the relevant SDK license terms yourself; the SDK and its license are not bundled with this repository.

The included `build.ps1` compiles the worker and application using `asconfig.json`, then packages a portable Windows x64 application with the captive AIR runtime. SDKs, package caches, certificates, and generated binaries are not committed.

The locally verified toolchain is:

- Eclipse Temurin JDK 17.0.20.1+1.
- Apache Flex SDK 4.16.1 overlaid with HARMAN AIR SDK for Flex 51.2.2.7.
- Apache Ant 1.10.18 for SDK preparation.
- Node.js with a local asconfigc 1.9.0 installation.

By default, the script expects this layout next to the checkout. All of these directories can be placed on another drive:

```text
workspace/
  ObjectBuilderV2/                 # this checkout (the folder name may differ)
    build.ps1
    asconfig.json
  ObjectBuilder-tools/
    flex-air/                     # prepared Flex + AIR SDK
    java/<jdk-folder>/bin/java.exe # exactly one extracted JDK
    node/node_modules/asconfigc/  # local npm installation
    tmp/                          # created automatically
```

Install the build helper locally, directing the npm cache to the same drive:

```powershell
npm.cmd install --prefix ..\ObjectBuilder-tools\node --cache ..\ObjectBuilder-tools\npm-cache --ignore-scripts --no-audit --no-fund asconfigc@1.9.0
```

From the checkout, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

`ExecutionPolicy Bypass` applies only to that process. The script uses process-local Java and temporary-directory settings and restores them afterward; it does not modify the global PATH. To select another prepared toolchain, pass `-ToolsRoot 'D:\your-tools-folder'`. Use `-CompileOnly` to generate the SWFs or `-PackageOnly` to package existing compiled output.

The executable is generated at `bin/ObjectBuilder/ObjectBuilder.exe`. Keep the entire `bin/ObjectBuilder` directory together, including its AIR runtime, icons, and configuration. The local AIR package uses a temporary self-signed certificate that is deleted after packaging; it is not a trusted Windows publisher signature or a production signing configuration. The running application may still save preferences in the normal Windows AppData directory.

Compilation and packaging have been verified locally. Interactive DAT/SPR editing has not been tested as part of this import.


Donate
----
[![Donate](https://www.paypalobjects.com/en_US/i/btn/btn_donate_LG.gif)](https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=QFNUYQ24ULK7S)
