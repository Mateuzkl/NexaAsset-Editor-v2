package ob.studio {
    import flash.utils.ByteArray;
    import mx.utils.Base64Encoder;
    import mx.utils.Base64Decoder;
    public class StudioPixels {
        public static function decode(value:String):ByteArray { var d:Base64Decoder=new Base64Decoder();d.decode(value);return d.toByteArray(); }
        public static function encode(bytes:ByteArray):String {var e:Base64Encoder=new Base64Encoder();e.insertNewLines=false;e.encodeBytes(bytes);return e.toString();}
    }
}
