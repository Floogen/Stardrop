This script builds the app, codesigns it, notarizes it, packages it into a disk image for intuitive installation, and notarizes that dmg.

To properly build and codesign for MacOS, this script needs to be run by someone with an Apple Developer Program account.
As this costs $99 a year to obtain, builds are currently made by @gdmagana.
The Stardrop.csproj also must be replaced in the original project so that both architectures can be targeted.

usage:
`./build-mac --arch osx-arm64` (leave blank to build both)
