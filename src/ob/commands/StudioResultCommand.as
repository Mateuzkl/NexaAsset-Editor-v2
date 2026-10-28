package ob.commands {
    import com.mignari.workers.WorkerCommand;
    public class StudioResultCommand extends WorkerCommand {
        public function StudioResultCommand(id:String,result:String,error:String=null) { super(id,result,error); }
    }
}
