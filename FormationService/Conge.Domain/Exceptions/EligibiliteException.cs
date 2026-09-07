namespace Conge.Domain.Exceptions;

public class EligibiliteException : Exception
{
    public Guid EmployeId { get; }

    public EligibiliteException(Guid employeId, string raison)
        : base(raison)
    {
        EmployeId = employeId;
    }
}
