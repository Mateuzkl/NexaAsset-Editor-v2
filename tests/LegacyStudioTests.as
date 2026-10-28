package {
    import flash.filesystem.File;
    import flash.utils.ByteArray;
    import mx.utils.UIDUtil;
    import ob.settings.ObjectBuilderSettings;
    import ob.studio.*;
    import otlib.core.*;
    import otlib.things.*;
    import otlib.sprites.SpriteStorage;
    import otlib.animation.FrameGroup;
    public class LegacyStudioTests {
        public static function run(folder:File):void {
            var version:Version=VersionStorage.getInstance().getByValueString("10.98");
            var features:ClientFeatures=new ClientFeatures(true,true,true,true);
            var things:ThingTypeStorage=new ThingTypeStorage(new ObjectBuilderSettings()),sprites:SpriteStorage=new SpriteStorage();
            things.createNew(version,features);sprites.createNew(version,features);
            var initialSpriteCount:uint=sprites.spritesCount;
            var adapter:LegacyStudioAdapter=new LegacyStudioAdapter(things,sprites,features),cells:Array=[];
            for(var cell:int=0;cell<12;cell++){
                var rgba:ByteArray=new ByteArray();
                for(var y:int=0;y<64;y++)for(var x:int=0;x<64;x++){rgba.writeByte(cell+1);rgba.writeByte(x);rgba.writeByte(y);rgba.writeByte(x%3==0?255:127);}
                cells.push({rgba:StudioPixels.encode(rgba),duration:120+int(cell/4)});rgba.clear();
            }
            var packet:Object={schemaVersion:1,expectedRevision:8,editId:UIDUtil.createUID(),category:"outfit",width:64,height:64,directions:4,frames:cells};
            var result:Object=adapter.execute("commit_art",packet,8),id:uint=result.selected;
            var thing:ThingType=things.getThingType(id,"outfit"),group:FrameGroup=thing.frameGroups[1];
            check(thing.frameGroups[0].frames==1&&group.frames==3&&group.patternX==4&&group.width==2&&group.height==2,"Legacy idle/walk geometry");
            check(group.spriteIndex.length==48&&group.frameDurations[2].minimum==122,"Legacy sprite references/timing");
            check(adapter.execute("commit_art",packet,9).selected==id&&sprites.spritesCount==initialSpriteCount+48,"Legacy idempotence");
            check(adapter.execute("list_assets",{category:"outfit",page:0,query:null},9).items.length>0,"Explicit null query hid all outfits");
            var before:uint=sprites.spritesCount;
            packet.editId=UIDUtil.createUID();packet.expectedRevision=9;cells[11].rgba="AA==";
            try{adapter.execute("commit_art",packet,9);throw new Error("Invalid legacy packet accepted");}catch(error:Error){check(error.message!="Invalid legacy packet accepted",error.message);}
            check(sprites.spritesCount==before&&things.getMaxId("outfit")==id,"Invalid legacy packet left partial assets");
            var dat:File=folder.resolvePath("studio-art.dat"),spr:File=folder.resolvePath("studio-art.spr");
            check(things.compile(dat,version,features)&&sprites.compile(spr,version,features),"Legacy animated compile");
            var reopened:ThingTypeStorage=new ThingTypeStorage(new ObjectBuilderSettings()),pixels:SpriteStorage=new SpriteStorage();
            reopened.load(dat,version,features);pixels.load(spr,version,features);
            group=reopened.getThingType(id,"outfit").frameGroups[1];
            check(group.frames==3&&group.patternX==4&&group.frameDurations[2].maximum==122,"Legacy animation did not reopen");
            // The fourth tile is top-left: x/y/alpha must survive the real compressor/reader.
            var tile:ByteArray=pixels.getPixelsStrict(group.spriteIndex[3]);
            check(tile[0]==255&&tile[1]==1&&tile[2]==0&&tile[3]==0&&tile[4]==127&&tile[5]==1&&tile[6]==1&&tile[7]==0,"Legacy tile order or straight ARGB changed");tile.clear();
            reopened.unload();pixels.unload();things.unload();sprites.unload();
            trace("PASS: legacy Studio 64x64, 4 directions x 3 frames, idle/walk groups, exact tiles/alpha, timings, rejected-batch integrity, idempotence and Compile/reopen");
        }
        private static function check(value:Boolean,message:String):void {if(!value)throw new Error(message);}
    }
}
