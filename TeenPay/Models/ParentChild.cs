namespace TeenPay.Models;

public class ParentChild
{
    public long Id { get; set; }
    public int ParentUserId { get; set; }
    public int ChildUserId { get; set; }
}

