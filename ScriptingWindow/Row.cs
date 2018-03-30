using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScriptingWindow
{
    public class Row
    {
        public Row() { }
        public Row(Row rhs)
        {
            foreach (var x in rhs._Values)
            {
                this._Values[x.Key] = x.Value;
            }
        }
        private Dictionary<string, object> _Values = new Dictionary<string, object>();
        public IDictionary<string, object> Values => _Values;

        public object this[string key]
        {
            get => _Values[key];
            set => _Values[key] = value;
        }


    }
}
