using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScriptingWindow
{
    [Serializable]
    public class Record
    {
        /// <summary>
        ///  create me
        /// </summary>
        public Record() { }
        public Record(Record rhs)
        {
            this._RecordTime = rhs._RecordTime;
            foreach (Row row in rhs._Rows)
            {
                this._Rows.Add(new Row(row));
            }
        }

        private DateTime _RecordTime = new DateTime();
        public DateTime RecordTime
        {
            get => _RecordTime;
            set => _RecordTime = value;
        }
        
        private List<Row> _Rows = new List<Row>();
        public IList<Row> Rows => _Rows;

        /// <summary>
        /// do a thing
        /// </summary>
        public void Foo() { }
    }
}
