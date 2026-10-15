package ob.commands.things
{
    import com.mignari.workers.WorkerCommand;
    public class ExportNativeObjectCommand extends WorkerCommand
    {
        public function ExportNativeObjectCommand(model:Object,path:String,source:String,obd:uint,version:uint,transparent:Boolean,quality:uint,job:uint=0)
        {super(model,path,source,obd,version,transparent,quality,job);}
    }
}
