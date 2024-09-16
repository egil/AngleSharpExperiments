using Microsoft.AspNetCore.Components;

namespace AngleSharpExperiments;

public class ChangeEventArgs<T> : ChangeEventArgs
    where T : IFormattable
{
    private T? value;

    public new T? Value
    {
        get
        {
            return value;
        }
        set
        {
            this.value = value;
            base.Value = value?.ToString(null, null);
        }
    }
}
