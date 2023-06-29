
namespace Sharpino.Lib.EvStore;
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;


public class Option<T>
{
    private readonly T _value;
    private readonly bool _hasValue;

    private Option(T value)
    {
        _value = value;
        _hasValue = true;
    }

    private Option()
    {
        _hasValue = false;
    }

    public static Option<T> Some(T value)
    {
        return new Option<T>(value);
    }

    public static Option<T> None()
    {
        return new Option<T>();
    }

    public bool HasValue => _hasValue;

    public T Value
    {
        get
        {
            if (!_hasValue)
            {
                throw new InvalidOperationException("Option has no value.");
            }
            return _value;
        }
    }
}


