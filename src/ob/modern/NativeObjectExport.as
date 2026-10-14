package ob.modern
{
    import flash.filesystem.File;
    import flash.filesystem.FileMode;
    import flash.filesystem.FileStream;
    import flash.utils.ByteArray;
    import flash.utils.Dictionary;
    import flash.utils.Endian;
    import flash.display.BitmapData;
    import otlib.animation.FrameGroup;
    import otlib.animation.FrameDuration;
    import otlib.things.ThingType;
    import otlib.things.ThingData;
    import otlib.things.ThingSerializer;
    import otlib.sprites.SpriteData;
    import otlib.obd.OBDEncoder;
    import ob.settings.ObjectBuilderSettings;
    import nail.image.ImageCodec;

    /** Uses the existing OBD/image encoder on profile-normalized native data. */
    public final class NativeObjectExport
    {
        public static function packet(path:String):ByteArray {
            var stream:FileStream=new FileStream(),bytes:ByteArray=new ByteArray();
            try{stream.open(new File(path),FileMode.READ);if(stream.bytesAvailable>20*1024*1024)throw new Error("Native OBD packet exceeds 20 MiB.");stream.readBytes(bytes);}finally{stream.close();}
            return bytes;
        }
        public static function data(model:Object,obd:uint,version:uint):ThingData
        {
            var stream:ByteArray=model.pixels as ByteArray,temporary:Boolean=stream==null;
            if(temporary)stream=packet(model.file);
            var originalPosition:uint=stream.position;var originalEndian:String=stream.endian;stream.position=0;
            var pixels:Object={};
            try {
                stream.endian=Endian.LITTLE_ENDIAN;
                if(stream.bytesAvailable>20*1024*1024 || stream.bytesAvailable<8 || stream.readUnsignedInt()!=0x3142584E)throw new Error("Invalid native OBD pixel packet.");
                var count:uint=stream.readUnsignedInt();if(count>4097)throw new Error("Native OBD packet exceeds its sprite limit.");
                for(var i:uint=0;i<count;i++){
                    if(stream.bytesAvailable<16)throw new Error("Truncated native OBD packet.");
                    var id:uint=stream.readUnsignedInt(),w:int=stream.readInt(),h:int=stream.readInt(),size:int=stream.readInt();
                    if(w!=32||h!=32||size!=4096||stream.bytesAvailable<size||pixels[id])throw new Error("Invalid normalized OBD sprite.");
                    var bytes:ByteArray=new ByteArray();stream.readBytes(bytes,0,size);pixels[id]=bytes;
                }
                if(stream.bytesAvailable)throw new Error("Trailing native OBD pixels.");
            }finally{if(temporary)stream.clear();else{stream.position=originalPosition;stream.endian=originalEndian;}}
            var thing:ThingType=new ThingType();thing.id=model.id;thing.category=model.category;
            bytes=new ByteArray();bytes.endian=Endian.LITTLE_ENDIAN;
            var hex:String=model.properties;if(hex.length>131072||hex.length%2||!/^[0-9a-f]+$/i.test(hex))throw new Error("Invalid normalized OBD properties.");
            for(i=0;i<hex.length;i+=2)bytes.writeByte(parseInt(hex.substr(i,2),16));bytes.position=0;
            var read:Function=ThingSerializer["readProperties"+model.family];
            if(read==null||!read(thing,bytes)||bytes.bytesAvailable)throw new Error("Invalid normalized property contract.");
            var groups:Array=model.groups as Array;if(!groups||groups.length<1||groups.length>2)throw new Error("Invalid OBD frame groups.");
            if(obd<300 && groups.length>1)groups=[groups[groups.length-1]];
            var sprites:Dictionary=new Dictionary();
            for(i=0;i<groups.length;i++){
                var row:Object=groups[i],g:FrameGroup=new FrameGroup();g.type=i;g.width=row.width;g.height=row.height;g.exactSize=row.exactSize;
                g.layers=row.layers;g.patternX=row.patternX;g.patternY=row.patternY;g.patternZ=row.patternZ;g.frames=row.frames;
                g.isAnimation=g.frames>1;g.animationMode=row.mode;g.loopCount=row.loops;g.startFrame=row.start;
                g.spriteIndex=Vector.<uint>(row.references);if(g.getTotalSprites()!=g.spriteIndex.length||g.spriteIndex.length>4096)throw new Error("Invalid normalized OBD references.");
                g.frameDurations=new Vector.<FrameDuration>();for(var phase:int=0;phase<g.frames;phase++){var duration:Object=row.phases.length>phase?row.phases[phase]:null;g.frameDurations.push(new FrameDuration(duration&&duration.min!=null?duration.min:100,duration&&duration.max!=null?duration.max:100));}
                var list:Vector.<SpriteData>=new Vector.<SpriteData>();
                for each(id in g.spriteIndex){if(!pixels[id])throw new Error("Missing normalized sprite "+id);var sprite:SpriteData=new SpriteData();sprite.id=id;sprite.pixels=pixels[id];list.push(sprite);}
                sprites[i]=list;thing.setFrameGroup(i,g);
            }
            return ThingData.create(obd,version,thing,sprites);
        }
        public static function write(model:Object,path:String,sourceDirectory:String,obd:uint,version:uint,transparent:Boolean,quality:uint,settings:ObjectBuilderSettings):void
        {
            var file:File=new File(path),source:File=new File(sourceDirectory);
            if(file.exists||file.nativePath.toLowerCase().indexOf(source.nativePath.toLowerCase()+File.separator)==0||file.nativePath.toLowerCase()==source.nativePath.toLowerCase())throw new Error("Choose a new export file outside source assets.");
            var object:ThingData=data(model,obd,version),bytes:ByteArray,bitmap:BitmapData;
            var format:String=file.extension.toLowerCase();
            try{if(format=="obd")bytes=new OBDEncoder(settings).encode(object);else{bitmap=object.getTotalSpriteSheet(null,transparent?0x00FF00FF:0xFFFF00FF);bytes=ImageCodec.encode(bitmap,format,quality);}}
            finally{if(bitmap)bitmap.dispose();}
            if(!bytes)throw new Error("Native object export did not produce bytes.");
            var stream:FileStream=new FileStream(),stage:File=new File(file.nativePath+".partial"),owned:Boolean=false;
            if(stage.exists)throw new Error("Export staging file already exists.");
            try{stream.open(stage,FileMode.WRITE);owned=true;stream.writeBytes(bytes);stream.close();stage.moveTo(file,false);}
            catch(error:Error){try{stream.close();if(owned&&stage.exists)stage.deleteFile();}catch(cleanup:Error){}throw error;}
            finally{stream.close();bytes.clear();}
        }
    }
}
