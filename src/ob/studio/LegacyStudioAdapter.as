package ob.studio {
    import flash.utils.ByteArray;
    import otlib.core.otlib_internal;
    import otlib.core.ClientFeatures;
    import otlib.sprites.SpriteStorage;
    import otlib.sprites.Sprite;
    import otlib.things.ThingTypeStorage;
    import otlib.things.ThingType;
    import otlib.animation.FrameGroup;
    import otlib.animation.FrameDuration;
    import otlib.utils.SpriteExtent;
    import otlib.utils.ChangeResult;
    use namespace otlib_internal;

    /** Runs only on the owning worker, serialized with normal editor commands. */
    public class LegacyStudioAdapter {
        private var things:ThingTypeStorage;
        private var sprites:SpriteStorage;
        private var features:ClientFeatures;
        private var commits:Object={};
        public function LegacyStudioAdapter(t:ThingTypeStorage,s:SpriteStorage,f:ClientFeatures){things=t;sprites=s;features=f;}
        public function owns(t:ThingTypeStorage):Boolean{return t===things;}
        private function category(value:String):void {if(["item","outfit","effect","missile"].indexOf(value)<0)throw new Error("Invalid category.");}
        public function execute(method:String,p:Object,revision:Number):Object {
            if(!things||!things.loaded||!sprites||!sprites.loaded)throw new Error("Open an asset project first.");
            if(method=="get_project_info")return {revision:revision,format:"legacy",clientVersion:things.version?things.version.value:0,spriteCount:sprites.spritesCount,width:SpriteExtent.DEFAULT_SIZE,transparency:features.transparency,capabilities:{read:true,drafts:true,newAsset:SpriteExtent.DEFAULT_SIZE==32,animatedNewAsset:SpriteExtent.DEFAULT_SIZE==32,replace:false,save:false,destructive:false,maxCells:64,maxLayers:16,maxNativeId:65535,timing:features.improvedAnimations}};
            if(method=="get_sprite"){
                if(!sprites.hasSpriteId(uint(p.spriteId)))throw new Error("Sprite does not exist.");
                var raw:ByteArray=sprites.getPixelsStrict(uint(p.spriteId));var encoded:String=StudioPixels.encode(raw);raw.clear();
                return {spriteId:p.spriteId,width:SpriteExtent.DEFAULT_SIZE,height:SpriteExtent.DEFAULT_SIZE,argb:encoded};
            }
            if(method=="list_assets"){
                if(p.page<0||p.page>100000||String(p.query).length>256)throw new Error("Invalid page/query.");
                var list:Array=[],start:int=int(p.page)*100,seen:int=0,q:String=String(p.query).toLowerCase();
                if(p.category=="sprite"){for(var sid:uint=uint(start);sid<=sprites.spritesCount && list.length<100;sid++)list.push({id:sid});return {items:list,page:p.page};}
                category(p.category);
                for(var id:uint=things.getMinId(p.category);id<=things.getMaxId(p.category)&&list.length<100;id++){
                    var t:ThingType=things.getThingType(id,p.category);if(!t)continue;
                    if(q.length&&String(id).indexOf(q)<0&&String(t.name||"").toLowerCase().indexOf(q)<0)continue;
                    if(seen++<start)continue;list.push({id:id,name:t.name,category:p.category});
                }return {items:list,page:p.page};
            }
            if(method=="get_asset"||method=="get_frame"){
                category(p.category);t=things.getThingType(uint(p.assetId),p.category);if(!t)throw new Error("Asset does not exist.");
                var remaining:int=4096;var groups:Array=[];for each(var g:FrameGroup in t.frameGroups){if(!g)continue;var refs:Array=[];var take:int=Math.min(remaining,g.spriteIndex.length);remaining-=take;for(var ri:int=0;ri<take;ri++)refs.push(g.spriteIndex[ri]);var phases:Array=[];if(g.frameDurations)for each(var duration:FrameDuration in g.frameDurations)phases.push({min:duration.minimum,max:duration.maximum});groups.push({index:g.type,width:g.width,height:g.height,layers:g.layers,patternX:g.patternX,patternY:g.patternY,patternZ:g.patternZ,frames:g.frames,references:refs,referenceCount:g.spriteIndex.length,referencesTruncated:take<g.spriteIndex.length,phases:phases});}
                if(method=="get_asset")return {assetId:t.id,category:t.category,name:t.name,groups:groups};
                g=t.frameGroups[int(p.group)];if(!g||p.frame<0||p.frame>=g.frames||p.direction<0||p.direction>=g.patternX||p.layer<0||p.layer>=g.layers)throw new Error("Invalid frame selection.");
                refs=[];for(var yy:int=0;yy<g.height;yy++)for(var xx:int=0;xx<g.width;xx++)refs.push(g.spriteIndex[g.getSpriteIndex(xx,yy,p.layer,p.direction,0,0,p.frame)]);
                return {assetId:t.id,group:p.group,frame:p.frame,direction:p.direction,layer:p.layer,references:refs};
            }
            if(method=="commit_art")return commit(p,revision);
            throw new Error("Unsupported studio command.");
        }
        private function commit(p:Object,revision:Number):Object {
            var prior:Object=commits[p.editId];if(prior)return prior;
            if(p.schemaVersion!=1||p.expectedRevision!==revision)throw new Error("PROJECT_CONFLICT: editor revision changed before commit.");category(p.category);
            var w:int=int(p.width),h:int=int(p.height),directions:int=int(p.directions),cells:Array=p.frames as Array,size:int=SpriteExtent.DEFAULT_SIZE;
            if((w!=32&&w!=64)||(h!=32&&h!=64)||(directions!=1&&directions!=4)||!cells||cells.length<1||cells.length>64||cells.length%directions||size!=32)throw new Error("Studio supports standard 32-pixel legacy tiles and 32/64-pixel art, 1/4 directions and at most 64 cells.");
            if(things.getMaxId(p.category)>=65535)throw new Error("Target category ID limit reached.");
            var count:int=cells.length*(w/size)*(h/size),limit:Number=features.extended?uint.MAX_VALUE:65535;
            if(Number(sprites.spritesCount)+count>limit)throw new Error("Target SPR ID limit reached.");
            var staged:Vector.<ByteArray>=new Vector.<ByteArray>(),frameCount:int=cells.length/directions;
            var thing:ThingType=new ThingType();thing.category=p.category;
            var group:FrameGroup=new FrameGroup();group.width=w/size;group.height=h/size;group.exactSize=Math.max(w,h);group.patternX=directions;group.frames=frameCount;group.isAnimation=frameCount>1;group.spriteIndex=new Vector.<uint>();group.frameDurations=new Vector.<FrameDuration>();
            try {
                for(var i:int=0;i<cells.length;i++){
                    var ms:int=int(cells[i].duration);if(ms<10||ms>10000)throw new Error("Invalid frame duration.");
                    if(i%directions==0)group.frameDurations.push(new FrameDuration(ms,ms));else if(ms!=int(cells[i-i%directions].duration))throw new Error("Direction timings must match.");
                    if(!features.improvedAnimations&&ms!=100)throw new Error("This legacy version cannot store phase timings. Use 100 ms fallback or a version with improved animations.");
                    var rgba:ByteArray=StudioPixels.decode(cells[i].rgba);if(rgba.length!=w*h*4)throw new Error("Invalid frame pixels.");
                    // Legacy tiles are stored from the bottom-right of the rendered frame.
                    for(var ty:int=0;ty<group.height;ty++)for(var tx:int=0;tx<group.width;tx++){
                        var tile:ByteArray=new ByteArray(),ox:int=w-(tx+1)*size,oy:int=h-(ty+1)*size;
                        for(var y:int=0;y<size;y++)for(var x:int=0;x<size;x++){var at:int=((oy+y)*w+ox+x)*4;tile.writeByte(rgba[at+3]);tile.writeByte(rgba[at]);tile.writeByte(rgba[at+1]);tile.writeByte(rgba[at+2]);}
                        tile.position=0;staged.push(tile);
                    }rgba.clear();
                }
                // No storage mutation has happened before validation completes.
                var previousCount:uint=sprites._spritesCount;
                try {
                    for each(tile in staged){var added:ChangeResult=sprites.internalAddSprite(tile);if(!added.done)throw new Error(added.message);group.spriteIndex.push(sprites.spritesCount);}
                    if(p.category=="outfit"&&features.frameGroups&&frameCount>1){
                        var idle:FrameGroup=group.clone();idle.frames=1;idle.isAnimation=false;idle.spriteIndex=group.spriteIndex.slice(0,group.width*group.height*directions);idle.frameDurations=new Vector.<FrameDuration>();idle.frameDurations.push(group.frameDurations[0].clone());thing.setFrameGroup(0,idle);thing.setFrameGroup(1,group);
                    }else thing.setFrameGroup(0,group);
                    var result:ChangeResult=things.addThing(thing,p.category);if(!result.done)throw new Error(result.message);
                }catch(error:Error){for(var id:uint=previousCount+1;id<=sprites._spritesCount;id++){var sprite:Sprite=sprites._sprites[id];if(sprite)sprite.dispose();delete sprites._sprites[id];}sprites._spritesCount=previousCount;throw error;}
                sprites.invalidate();things.invalidate();
                prior={selected:thing.id,sprite:previousCount+1,category:p.category,editId:p.editId};commits[p.editId]=prior;return prior;
            } finally {if(rgba)rgba.clear();for each(var bytes:ByteArray in staged)bytes.clear();}
            return null;
        }
    }
}
