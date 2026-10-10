package {
    import flash.display.Sprite;
    import flash.display.DisplayObject;
    import flash.display.BitmapData;
    import flash.display.PNGEncoderOptions;
    import flash.geom.Matrix;
    import flash.filesystem.File;
    import flash.filesystem.FileMode;
    import flash.filesystem.FileStream;
    import flash.events.InvokeEvent;
    import flash.desktop.NativeApplication;
    import flash.utils.ByteArray;
    import flash.utils.Endian;

    /** Rasterize the shared vector mark for AIR packaging. No image-editor/runtime dependency. */
    public class GenerateNexaIcons extends Sprite {
        [Embed(source="../assets/nexa/nexa-logo.svg")] private static const Logo:Class;
        public function GenerateNexaIcons() {
            NativeApplication.nativeApplication.addEventListener(InvokeEvent.INVOKE, generate);
        }
        private function generate(event:InvokeEvent):void {
            try {
                if (event.arguments.length != 1) throw new Error("Expected icon output directory.");
                var output:File = new File(event.arguments[0]);
                output.createDirectory();
                var mark:DisplayObject = new Logo();
                var payloads:Object = {};
                for each (var size:int in [16, 32, 36, 48, 72, 96, 114, 128, 144, 152, 256, 512]) {
                    var bitmap:BitmapData = new BitmapData(size, size, true, 0);
                    bitmap.draw(mark, new Matrix(size / 256, 0, 0, size / 256), null, null, null, true);
                    var png:ByteArray = bitmap.encode(bitmap.rect, new PNGEncoderOptions());
                    bitmap.dispose();
                    payloads[size] = png;
                    write(output.resolvePath("icon" + size + ".png"), png);
                }
                var sizes:Array = [16, 32, 48, 256];
                var ico:ByteArray = new ByteArray(); ico.endian = Endian.LITTLE_ENDIAN;
                ico.writeShort(0); ico.writeShort(1); ico.writeShort(sizes.length);
                var offset:uint = 6 + sizes.length * 16;
                for each (size in sizes) {
                    png = payloads[size];
                    ico.writeByte(size == 256 ? 0 : size); ico.writeByte(size == 256 ? 0 : size);
                    ico.writeByte(0); ico.writeByte(0); ico.writeShort(1); ico.writeShort(32);
                    ico.writeUnsignedInt(png.length); ico.writeUnsignedInt(offset); offset += png.length;
                }
                for each (size in sizes) ico.writeBytes(payloads[size]);
                write(output.resolvePath("NexaAsset Editor v2.ico"), ico);
                trace("PASS: Nexa vector mark exported to PNG sizes and Windows ICO.");
                NativeApplication.nativeApplication.exit(0);
            } catch (error:Error) {
                trace(error.getStackTrace()); NativeApplication.nativeApplication.exit(1);
            }
        }
        private function write(file:File, data:ByteArray):void {
            var stream:FileStream = new FileStream(); stream.open(file, FileMode.WRITE);
            try { stream.writeBytes(data); } finally { stream.close(); }
        }
    }
}
