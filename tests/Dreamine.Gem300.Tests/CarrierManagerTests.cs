using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Carrier;
using Dreamine.Gem300.Infrastructure;
using Xunit;

namespace Dreamine.Gem300.Tests;

public sealed class CarrierManagerTests
{
    [Fact]
    public void VerifiedCarrierTraversesAccessAndRemovalLifecycle()
    {
        var manager = CreateReadyPort();
        manager.Reserve("P1"); manager.CancelReservation("P1"); manager.Bind("P1", "C1", 2); manager.BeginLoad("P1"); manager.CompleteLoad("P1");
        manager.WaitForIdDecision("C1"); manager.AcceptId("C1"); manager.WaitForSlotMapDecision("C1", new[] { CarrierSlotState.CorrectlyOccupied, CarrierSlotState.Empty }); manager.AcceptSlotMap("C1");
        manager.BeginAccess("C1"); manager.CompleteAccess("C1"); manager.PrepareUnload("C1"); manager.BeginUnload("P1"); manager.CompleteUnload("P1");
        var port = manager.GetLoadPort("P1"); Assert.Equal(LoadPortTransferState.ReadyToLoad, port.TransferState); Assert.Equal(CarrierAssociationState.NotAssociated, port.AssociationState); Assert.Throws<KeyNotFoundException>(() => manager.GetCarrier("C1"));
    }

    [Fact]
    public void CarrierMaintainsOrthogonalVerificationAndAccessStates()
    {
        var manager = CreateLoadedCarrier(); manager.AcceptId("C1"); manager.WaitForSlotMapDecision("C1", new[] { CarrierSlotState.Empty, CarrierSlotState.Empty });
        var snapshot = manager.GetCarrier("C1"); Assert.Equal(CarrierIdStatus.VerificationOk, snapshot.IdStatus); Assert.Equal(CarrierSlotMapStatus.WaitingForHost, snapshot.SlotMapStatus); Assert.Equal(CarrierAccessingStatus.NotAccessed, snapshot.AccessingStatus);
        Assert.Throws<InvalidOperationException>(() => manager.BeginAccess("C1"));
    }

    [Fact]
    public void RejectedIdCanBeUnloadedWithoutAccess()
    {
        var manager = CreateLoadedCarrier(); manager.WaitForIdDecision("C1"); manager.RejectId("C1"); manager.PrepareUnload("C1"); manager.BeginUnload("P1"); manager.CompleteUnload("P1");
        Assert.Equal(LoadPortTransferState.ReadyToLoad, manager.GetLoadPort("P1").TransferState);
    }

    [Fact]
    public void SlotMapLengthMustMatchCarrierCapacity()
    {
        var manager = CreateLoadedCarrier();
        Assert.Throws<ArgumentException>(() => manager.WaitForSlotMapDecision("C1", new[] { CarrierSlotState.Empty }));
    }

    [Fact]
    public void PrematureUnloadCompletionIsRejected()
    {
        var manager = CreateLoadedCarrier(); Assert.Throws<InvalidOperationException>(() => manager.CompleteUnload("P1"));
    }

    [Fact]
    public void LoadPortsHaveIndependentState()
    {
        var manager = new CarrierManager(new Gem300EventJournal()); manager.RegisterLoadPort("P1"); manager.RegisterLoadPort("P2", LoadPortAccessMode.Manual); manager.SetInService("P1");
        Assert.Equal(LoadPortTransferState.ReadyToLoad, manager.GetLoadPort("P1").TransferState); Assert.Equal(LoadPortTransferState.OutOfService, manager.GetLoadPort("P2").TransferState);
    }

    private static CarrierManager CreateReadyPort() { var manager = new CarrierManager(new Gem300EventJournal()); manager.RegisterLoadPort("P1"); manager.SetInService("P1"); return manager; }
    private static CarrierManager CreateLoadedCarrier() { var manager = CreateReadyPort(); manager.Bind("P1", "C1", 2); manager.BeginLoad("P1"); manager.CompleteLoad("P1"); return manager; }
}
