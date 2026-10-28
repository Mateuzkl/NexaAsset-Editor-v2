package ob.studio {
    import flash.filesystem.*;
    import flash.utils.ByteArray;
    import mx.utils.UIDUtil;

    /** Export editable documents one at a time; acknowledge only bytes successfully written. */
    public class StudioDraftExport {
        public static function saveAll(controller:StudioController,directory:File,callback:Function,active:Function=null):void {
            controller.call("list_art_documents",{},function(value:Object,error:String):void {
                if(active!=null&&!active())return;
                if(error){callback(error);return;}
                var rows:Array=[];for each(var row:Object in value.documents)if(row.dirty)rows.push(row);
                var index:int=0;
                function next():void {
                    if(active!=null&&!active())return;
                    if(index==rows.length){callback(null);return;}
                    var entry:Object=rows[index++];
                    controller.call("ui_sidecar",{editId:entry.editId},function(result:Object,failure:String):void {
                        if(active!=null&&!active())return;
                        if(failure){callback(failure);return;}
                        try {writeNew(directory,entry,result.data);}catch(problem:Error){callback(problem.message);return;}
                        controller.call("ui_mark_exported",{editId:entry.editId,documentRevision:result.documentRevision,approvalRequested:result.approvalRequested},function(saved:Object,issue:String):void {
                            if(issue||saved.dirty){callback(issue||"Draft changed while saving. Save again before continuing.");return;}next();
                        });
                    });
                }
                next();
            });
        }
        public static function writeNew(directory:File,entry:Object,data:String):File {
            if(!directory.exists||!directory.isDirectory)throw new Error("Choose an existing folder for the drafts.");
            var name:String=String(entry.name||entry.category||"draft").replace(/[^a-zA-Z0-9_-]/g,"_").substr(0,40);
            var unique:String=UIDUtil.createUID().split("-").join("").toLowerCase();
            var target:File=directory.resolvePath("draft-"+name+"-"+unique+".nexaart");
            var stage:File=directory.resolvePath(".nexa-"+unique+".tmp"),owned:Boolean=false;
            var bytes:ByteArray=StudioPixels.decode(data),stream:FileStream=new FileStream();
            try {
                if(stage.exists||target.exists)throw new Error("Draft filename collision. Choose Save again.");
                stream.open(stage,FileMode.WRITE);owned=true;stream.writeBytes(bytes);stream.close();stage.moveTo(target,false);
            }finally{try{stream.close();}catch(ignore:Error){}bytes.clear();if(owned&&stage.exists)stage.deleteFile();}
            return target;
        }
    }
}
