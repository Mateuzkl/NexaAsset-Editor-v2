package ob.studio {
    import flash.utils.ByteArray;
    import mx.utils.Base64Encoder;
    import mx.utils.Base64Decoder;
    public class StudioPixels {
        public static function decode(value:String):ByteArray { var d:Base64Decoder=new Base64Decoder();d.decode(value);return d.toByteArray(); }
        public static function encode(bytes:ByteArray):String {var e:Base64Encoder=new Base64Encoder();e.insertNewLines=false;e.encodeBytes(bytes);return e.toString();}
        public static function fromRGBA(bytes:ByteArray):ByteArray {
            if(bytes.length%4)throw new Error("Invalid RGBA length.");var result:ByteArray=new ByteArray();
            for(var i:int=0;i<bytes.length;i+=4){result.writeByte(bytes[i+3]);result.writeByte(bytes[i]);result.writeByte(bytes[i+1]);result.writeByte(bytes[i+2]);}result.position=0;return result;
        }
        public static function toRGBA(bytes:ByteArray):ByteArray {
            if(bytes.length%4)throw new Error("Invalid ARGB length.");var result:ByteArray=new ByteArray();
            for(var i:int=0;i<bytes.length;i+=4){result.writeByte(bytes[i+1]);result.writeByte(bytes[i+2]);result.writeByte(bytes[i+3]);result.writeByte(bytes[i]);}result.position=0;return result;
        }
    }
}
