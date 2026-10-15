package
{
    import flash.filesystem.File;
    import flash.filesystem.FileMode;
    import flash.filesystem.FileStream;
    import flash.utils.ByteArray;
    import by.blooddy.crypto.MD5;
    import ob.modern.NativeObjectExport;
    import ob.settings.ObjectBuilderSettings;
    import otlib.core.Version;
    import otlib.core.ClientFeatures;
    import otlib.obd.OBDEncoder;
    import otlib.sprites.SpriteData;
    import otlib.sprites.SpriteStorage;
    import otlib.things.ThingData;
    import otlib.things.ThingTypeStorage;
    import otlib.animation.FrameGroup;
    import otlib.utils.OTFI;

    /** Existing AIR OBD and SPR codecs are the independent oracle for modern pixels. */
    public final class NativeTransparencyOracle
    {
        private static function check(ok:Boolean,message:String):void {if(!ok)throw new Error(message);}
        public static function run(root:File):int
        {
            var count:int=0;
            for each(var folder:File in root.getDirectoryListing())if(folder.isDirectory && folder.resolvePath("transparency.json").exists){
                test(folder);count++;
            }
            return count;
        }
        private static function test(folder:File):void
        {
            var file:FileStream=new FileStream();var expected:Object;
            try{file.open(folder.resolvePath("transparency.json"),FileMode.READ);expected=JSON.parse(file.readUTFBytes(file.bytesAvailable));}finally{file.close();}
            var version:Version=new Version();version.value=expected.version;version.datSignature=expected.dat;version.sprSignature=expected.spr;
            var run:File=folder.resolvePath("obd-oracle-"+new Date().time);run.createDirectory();
            var model:Object=expected.model,hashes:Object={};for each(var row:Object in expected.pixels)hashes[row.id]=row;
            model.pixels=NativeObjectExport.packet(model.file);model.file=null;
            var packet:ByteArray=model.pixels;var packetHash:String=MD5.hashBytes(packet);packet.position=3;
            // Borrowed packets must survive repeated exports, failed writes and failed parsing.
            var settings:ObjectBuilderSettings=new ObjectBuilderSettings(),encoder:OBDEncoder=new OBDEncoder(settings);
            for each(var obd:uint in [100,200,300]){
                var exported:File=run.resolvePath("v"+obd+".obd");
                NativeObjectExport.write(model,exported.nativePath,expected.source,obd,version.value,true,90,settings);
                check(packet.position==3 && MD5.hashBytes(packet)==packetHash,"Borrowed OBD packet was consumed");
                var data:ThingData=encoder.decodeFromFile(exported);
                for each(var rgba:Boolean in [false,true]){
                    var features:ClientFeatures=new ClientFeatures(false,rgba);features.applyVersionDefaults(version.value);
                    var storage:SpriteStorage=new SpriteStorage(),reopened:SpriteStorage=new SpriteStorage();storage.createNew(version,features);
                    var imported:Object={};
                    try{
                        // Use the same addSprite path as ObjectBuilderWorker.processThingDataSprites.
                        for each(var group:FrameGroup in data.thing.frameGroups)if(group){
                            for each(var sprite:SpriteData in data.sprites[group.type]){
                                check(MD5.hashBytes(sprite.pixels)==hashes[sprite.id].raw,"OBD changed source ARGB: "+sprite.id);
                                if(imported[sprite.id]!==undefined)continue;
                                check(storage.addSprite(sprite.pixels).done,"Legacy OBD sprite import failed");imported[sprite.id]=storage.spritesCount;
                                check(MD5.hashBytes(storage.getPixelsStrict(storage.spritesCount))==hashes[sprite.id][rgba ? "rgba" : "rgb"],"Imported transparent background became opaque");
                            }
                        }
                        var spr:File=run.resolvePath("v"+obd+(rgba ? "-rgba" : "-rgb")+".spr");check(storage.compile(spr,version,features),"Imported SPR compile failed");reopened.load(spr,version,features);check(reopened.loaded,"Imported SPR reopen failed");
                        for(var id:String in imported)check(MD5.hashBytes(reopened.getPixelsStrict(imported[id]))==hashes[id][rgba ? "rgba" : "rgb"],"Imported SPR transparency failed after reopening");
                    }finally{storage.unload();reopened.unload();}
                }
            }
            var invalid:Object={pixels:packet};var threw:Boolean=false;var first:int=packet[0];packet[0]=0;
            try{NativeObjectExport.data(invalid,300,version.value);}catch(error:Error){threw=true;}finally{packet[0]=first;}
            check(threw && packet.position==3 && MD5.hashBytes(packet)==packetHash,"Failed parsing destroyed borrowed pixels");packet.clear();
            for each(rgba in [false,true]){
                var directory:File=folder.resolvePath(rgba ? "direct-rgba" : "direct-rgb");var otfi:OTFI=new OTFI();check(otfi.load(directory.resolvePath("Tibia.otfi")),"Direct OTFI load");
                storage=new SpriteStorage();reopened=new SpriteStorage();var things:ThingTypeStorage=new ThingTypeStorage(settings);
                try{
                    things.load(directory.resolvePath("Tibia.dat"),version,otfi.features);storage.load(directory.resolvePath("Tibia.spr"),version,otfi.features);check(things.loaded && storage.loaded,"Direct DAT/SPR load");
                    var thingGroups:Array=[];for each(group in things.getThingType(model.id,model.category).frameGroups)if(group)thingGroups.push(group);
                    check(thingGroups.length==model.groups.length,"Direct frame group count");
                    for(var g:int=0;g<thingGroups.length;g++){
                        check(thingGroups[g].spriteIndex.length==model.groups[g].references.length,"Direct reference count");
                        for(var r:int=0;r<thingGroups[g].spriteIndex.length;r++)check(thingGroups[g].spriteIndex[r]==model.groups[g].references[r],"Direct reference order");
                    }
                    for each(row in expected.pixels)check(MD5.hashBytes(storage.getPixelsStrict(row.id))==row[rgba ? "rgba" : "rgb"],"Direct DAT/SPR transparency mismatch: "+row.id);
                    spr=directory.resolvePath("roundtrip.spr");check(storage.compile(spr,version,otfi.features),"Direct recompile");reopened.load(spr,version,otfi.features);
                    for each(row in expected.pixels)check(MD5.hashBytes(reopened.getPixelsStrict(row.id))==row[rgba ? "rgba" : "rgb"],"Direct transparency lost on recompile: "+row.id);
                }finally{storage.unload();reopened.unload();things.unload();}
            }
            trace("PASS transparency: "+folder.name+"; every referenced tile, OBD v1/v2/v3 import, RGB/RGBA SPR compile/reopen and direct DAT/SPR");
        }
    }
}
