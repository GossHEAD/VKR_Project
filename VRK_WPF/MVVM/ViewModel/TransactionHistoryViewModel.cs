using System.Collections.ObjectModel;
namespace VRK_WPF.MVVM.ViewModel;

public class TransactionHistoryViewModel
{
    public ObservableCollection<TransactionViewModel> Transactions { get; set; }

    public TransactionHistoryViewModel()
    {
        Transactions = new ObservableCollection<TransactionViewModel>
        {
            new TransactionViewModel { TransactionId = "T001", Sender = "Node1", Receiver = "Node2", Status = "Success", Timestamp = DateTime.Now },
            new TransactionViewModel { TransactionId = "T002", Sender = "Node3", Receiver = "Node1", Status = "Failed", Timestamp = DateTime.Now.AddMinutes(-5) }
        };
    }
}

public class TransactionViewModel
{
    public string TransactionId { get; set; }
    public string Sender { get; set; }
    public string Receiver { get; set; }
    public string Status { get; set; }
    public DateTime Timestamp { get; set; }
}
