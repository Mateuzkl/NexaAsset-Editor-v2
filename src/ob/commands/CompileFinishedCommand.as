package ob.commands
{
    import com.mignari.workers.WorkerCommand;
    public final class CompileFinishedCommand extends WorkerCommand
    {
        public function CompileFinishedCommand(success:Boolean, copyPath:String = null) { super(success, copyPath); }
    }
}
