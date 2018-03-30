using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Interfaces
{
    public interface IDataValue
    {
        Type ValueType { get; }
        object NullValue();
    }
    public interface IDataValue<T> : IDataValue
    {
        T Value { get; set; }
        T GetValue();
    }
    public interface IRow
    {
        IDictionary<string, IDataValue> Values { get; }
    }
    public interface IRecord
    {
        IList<IRow> Rows { get; }

    }

    public class DataValueStub<T> : IDataValue<T>
    {
        public DataValueStub() { }
        T m_Value = default(T);
        public T Value { get => m_Value; set => m_Value = value; }
        public T GetValue() { return m_Value; }
        public object NullValue() { return default(T); }
        public Type ValueType => typeof(T);
    }
    public class RowStub : IRow
    {
        public RowStub()
        {
            m_Values.Add("myValue", new DataValueStub<long>());
        }
        Dictionary<string, IDataValue> m_Values = new Dictionary<string, IDataValue>();
        public IDictionary<string, IDataValue> Values => m_Values;
    }
    public class RecordStub : IRecord
    {
        public RecordStub()
        {
            m_Rows.Add(new RowStub());
        }
        List<IRow> m_Rows = new List<IRow>();
        public IList<IRow> Rows => m_Rows;
    }

    public abstract class VBUserCodeContextBase
    {
        protected VBUserCodeContextBase(IRecord record)
        {
            currentRecord = record;
        }
        protected IRecord currentRecord;

        protected int ValueReference(string inputSource, string prn)
        {
            return 0;
        }

        void TestFunc()
        {
            string sys = "sysA";
            int x = ValueReference(sys, "Az");
        }
    }
}
