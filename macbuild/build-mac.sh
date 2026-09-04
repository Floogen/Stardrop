#!/bin/bash

APP_NAME="Stardrop"
HOME="/Users/gdmagana"
PROJECT_DIR="$HOME/Developer/Github/Stardrop/Stardrop"
OUTPUT_DIR="$HOME/Developer/Github/Stardrop/releases"
ASSETS_DIR="$PROJECT_DIR/Assets"
MAC_ASSETS_DIR="assets"
ENTITLEMENTS="$MAC_ASSETS_DIR/ents.entitlements"
SIGNING_IDENTITY="Developer ID Application: Gabriel Magaña (Z4D7MUNZ97)"
DMG_BACKGROUND="$MAC_ASSETS_DIR/background.png"

# Architectures to build for
DEFAULT_ARCHS=("osx-x64" "osx-arm64")
ARCHS=()

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --arch)
            if [[ -z "$2" || "$2" == --* ]]; then
                echo "Error: --arch requires an architecture value"
                exit 1
            fi
            ARCHS+=("$2")
            shift 2
            ;;
        --arch=*)
            arch_value="${1#*=}"
            if [[ -z "$arch_value" ]]; then
                echo "Error: --arch= requires an architecture value"
                exit 1
            fi
            ARCHS+=("$arch_value")
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [--arch architecture]"
            echo "  --arch architecture  Build for specific architecture (can be specified multiple times)"
            echo "                       Default: osx-x64 osx-arm64"
            exit 0
            ;;
        *)
            echo "Unknown parameter: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# If no architectures specified, use defaults
if [ ${#ARCHS[@]} -eq 0 ]; then
    ARCHS=("${DEFAULT_ARCHS[@]}")
fi

# Ensure output directory exists
mkdir -p "$OUTPUT_DIR"

for ARCH in "${ARCHS[@]}"; do
    echo "[INFO] Building for $ARCH..."
    mkdir -p "$OUTPUT_DIR/$ARCH"

    # Step 1: Build the project
    # dotnet publish "$PROJECT_DIR/Stardrop.csproj" -c Release -r $ARCH --self-contained || exit 1
    dotnet publish "$PROJECT_DIR/Stardrop.csproj" -r $ARCH -c Release /p:PublishSingleFile=true \
        /p:IncludeAllContentForSelfExtract=true /p:IncludeNativeLibrariesForSelfExtract=true \
        /p:EnableCompressionInSingleFile=true /p:PublishReadyToRun=true \
        -p:UseAppHost=true --self-contained true || exit 1
    # Check if the build was successful
    if [ $? -ne 0 ]; then
        echo "[ERROR] Build failed for $ARCH"
        exit 1
    fi
    echo "[INFO] Build successful for $ARCH"

    # Step 2: Create the .app bundle
    BUILD_DIR="$PROJECT_DIR/bin/Release/$ARCH/publish"
    APP_BUNDLE="$OUTPUT_DIR/$ARCH/$APP_NAME.app"
    rm -rf "$APP_BUNDLE"
    mkdir -p "$APP_BUNDLE/Contents/MacOS"
    mkdir -p "$APP_BUNDLE/Contents/Resources"

    # Copy executable and resources
    cp "$BUILD_DIR/$APP_NAME" "$APP_BUNDLE/Contents/MacOS/"
    cp "$BUILD_DIR/"*.dylib "$APP_BUNDLE/Contents/MacOS/"
    cp -R "$BUILD_DIR/Themes" "$APP_BUNDLE/Contents/MacOS/"
    cp -R "$BUILD_DIR/i18n" "$APP_BUNDLE/Contents/MacOS/"
    cp "$ASSETS_DIR/Info.plist" "$APP_BUNDLE/Contents/"
    cp "$ASSETS_DIR/Stardrop.icns" "$APP_BUNDLE/Contents/Resources/"

    # Step 3: Sign the .app bundle
    echo "[INFO] Signing the .app bundle for $ARCH..."
    find "$APP_BUNDLE/Contents/MacOS" -type f | while read -r fname; do
        codesign --force --timestamp --options=runtime --entitlements "$ENTITLEMENTS" --sign "$SIGNING_IDENTITY" "$fname"
    done
    codesign --force --timestamp --options=runtime --entitlements "$ENTITLEMENTS" --sign "$SIGNING_IDENTITY" "$APP_BUNDLE"

    # Step 4: Notarize the .app bundle
    echo "[INFO] Notarizing the .app bundle for $ARCH..."
    ZIP_FILE="$OUTPUT_DIR/$ARCH/$APP_NAME.zip"
    ditto -c -k --sequesterRsrc --keepParent "$APP_BUNDLE" "$ZIP_FILE"
    xcrun notarytool submit "$ZIP_FILE" --wait --keychain-profile Stardrop || exit 1
    xcrun stapler staple "$APP_BUNDLE"
    # Cleanup
    rm "$ZIP_FILE"
    
    # Step 5: Create a DMG file
    echo "[INFO] Creating a DMG file for $ARCH..."
    DMG_FILE="$OUTPUT_DIR/$ARCH/$APP_NAME.dmg"
    # Clean up any old temporary DMG files
    rm -f "$OUTPUT_DIR"/"$ARCH"/rw.*.dmg
    create-dmg \
        --icon-size 128 \
        --volname "$APP_NAME" \
        --text-size 16 \
        --icon "$APP_NAME.app" 200 150 \
        --app-drop-link 450 150 \
        --window-pos 200 200 \
        --window-size 650 376 \
        --background "$DMG_BACKGROUND" \
        --disk-image-size 150 \
        "$DMG_FILE" "$OUTPUT_DIR/$ARCH" \

    # Step 6: Notarize the DMG file
    echo "[INFO] Notarizing the DMG file for $ARCH..."
    xcrun notarytool submit "$DMG_FILE" --wait --keychain-profile Stardrop || exit 1
    xcrun stapler staple "$DMG_FILE"
done

echo "[INFO] Build and packaging complete. Output: $OUTPUT_DIR"
