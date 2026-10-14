package ob.commands.things
{
    import com.mignari.workers.WorkerCommand;
    public class NativeObjectExportResultCommand extends WorkerCommand
    {
        public function NativeObjectExportResultCommand(path:String,success:Boolean,message:String,job:uint){super(path,success,message,job);}
    }
}
