package ob.commands.things
{
    import com.mignari.workers.WorkerCommand;
    public class ExportLegacyModernCommand extends WorkerCommand
    {
        public function ExportLegacyModernCommand(ids:Vector.<uint>,category:String,directory:String,job:uint){super(ids,category,directory,job);}
    }
}
