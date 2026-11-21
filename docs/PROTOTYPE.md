# Disconnect-before-close prototype 3

This build starts from main v20 at e12602c. Step 1 has one added operation.
After closing Discord, it opens Proton's Home screen, invokes Disconnect
(or Cancel if a connection is in progress), waits for Unprotected and an
available Connect control, then uses the main version's normal Proton
shutdown routine.

If disconnection cannot be verified, step 1 reports the error and remains
available for retry. Its worker has a 150-second deadline for an unresponsive
automation provider. The automation targets the English Proton VPN 5.1.9 Home
screen, using the identifiers described in the supplied proposal.

Extract the whole ZIP and run **Anti-Janjanator 5000.exe**. Use its original
four numbered buttons. Step 3 launches Proton normally, following Proton's
own auto-connect preference. Check its connection before launching Discord.
The remembered first-time setup and the original settings-write guards apply.
See README.md and FIRST-LAUNCH.md for the main workflow.

Disconnection may permit direct connectivity depending on Kill Switch settings.
Resolve Proton login screens, modal dialogs, or unsupported status text before
retrying step 1.

The prototype is tested with simulated clients and UI states. The new live
disconnect/close sequence and traffic routing still need a controlled trial.
The prior prototype's UI-driven reconnect and graceful-menu-exit flows have
been replaced by the main branch implementation.
