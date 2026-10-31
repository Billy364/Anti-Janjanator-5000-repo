using System;
using DiscordProtonUpdater;
class GuiPolicyTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static int Main()
    {
        AccountInfo account = new AccountInfo { Ready = true };
        AppInspection state = new AppInspection { ScanReady = true }; state.Roots.Add("C:\\Fixture\\Discord");
        Check(!UiPolicy.CanRefresh(null, 2, false, account), "Unknown initial state unlocked refresh.");
        Check(!UiPolicy.CanRefresh(state, 1, false, account), "Single clear poll unlocked refresh.");
        Check(UiPolicy.CanRefresh(state, 2, false, account), "Two verified exit polls did not unlock refresh.");
        state.DiscordCount=1; Check(!UiPolicy.CanRefresh(state, 2, false, account) && UiPolicy.CanClose(state,false), "Discord-only running gate failed.");
        state.ProtonCount=1; Check(!UiPolicy.CanRefresh(state, 2, false, account), "Both-running gate failed.");
        state.DiscordCount=0; Check(!UiPolicy.CanRefresh(state, 2, false, account), "Proton-only running gate failed.");
        state.ProtonCount=0; state.UnknownCount=1; Check(!UiPolicy.CanRefresh(state, 2, false, account), "Unreadable status unlocked refresh.");
        state.UnknownCount=0; state.Known=false; Check(!UiPolicy.CanRefresh(state, 2, false, account), "Failed inspection unlocked refresh.");
        state.Known=true; Check(!UiPolicy.CanRefresh(state, 2, true, account) && !UiPolicy.CanClose(state,true), "Busy state permitted another operation.");
        Check(!UiPolicy.CanRefresh(state,2,false,null), "Missing account unlocked refresh.");
        account.NeedsSplitTunnelingActivation=true; Check(UiPolicy.CanRefresh(state,2,false,account), "Automatic activation account blocked refresh.");
        account.Ready=false; Check(!UiPolicy.CanRefresh(state,2,false,account), "Unsupported account unlocked refresh.");
        account.Ready=true; state.ScanReady=false; Check(!UiPolicy.CanRefresh(state,2,false,account), "Incomplete scan unlocked refresh.");
        Console.WriteLine("PASS: GUI enablement policy covers unknown, running, stopped, busy, helper/unknown, account, and scan states.");
        return 0;
    }
}
