namespace MRASM.com
{
    public class Symbol
    {
        private string name;
        private int type;
        private bool relocatable;
        private int value;
        
        public Symbol(string name, int type, bool relocatable, int value) {
            this.name = name;
            this.type = type;
            this.relocatable = relocatable;
            this.value = value;
        }

        public string Name {
            get => name;
            set => name = value;
        }

        public int Type {
            get => type;
            set => type = value;
        }

        public bool Relocatable {
            get => relocatable;
            set => relocatable = value;
        }

        public int Value {
            get => value;
            set => this.value = value;
        }
    }
}