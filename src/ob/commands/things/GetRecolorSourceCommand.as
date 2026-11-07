package ob.commands.things
{
    import com.mignari.workers.WorkerCommand;

    public class GetRecolorSourceCommand extends WorkerCommand
    {
        public function GetRecolorSourceCommand(request:uint, id:uint, category:String)
        {
            super(request, id, category);
        }
    }
}
