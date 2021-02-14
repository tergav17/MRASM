namespace MRASM.com
{
    public class Numeric {
        private int value;
        private int type;
        private bool relocatable;
        
        public Numeric(int value, int type) {
            this.value = value;
            this.type = type;
            this.relocatable = false;
        }
        
        public Numeric(int value, int type, bool relocatable) {
            this.value = value;
            this.type = type;
            this.relocatable = relocatable;
        }

        public int Value {
            get => value;
            set => this.value = value;
        }

        public int Type {
            get => type;
            set => type = value;
        }

        public bool Relocatable {
            get => relocatable;
            set => relocatable = value;
        }
    }
}