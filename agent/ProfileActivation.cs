// The saved default profile is requested on by default. Readiness gates and
// an explicit pause remain independent, so saving cannot undo emergency pause.
public sealed class ProfileActivation {
    public bool Verified,InputReady,MonitoringReady,Editing,BusySetup,Closing,Faulted,PauseRequested;
    public bool Active {get{return Verified && InputReady && MonitoringReady && !Editing && !BusySetup && !Closing && !Faulted && !PauseRequested;}}
}
