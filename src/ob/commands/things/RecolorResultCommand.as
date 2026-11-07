package ob.commands.things
{
    import com.mignari.workers.WorkerCommand;
    import otlib.things.ThingData;

    public class RecolorResultCommand extends WorkerCommand
    {
        public function RecolorResultCommand(request:uint, data:ThingData = null, revision:Number = 0, error:String = null)
        {
            super(request, data, revision, error);
        }
    }
}
