using System;
using System.Collections.Generic;
using System.IO;

namespace MRASM.com {
    
    //(M)ICRON (R)elative (As)se(m)bler
    public class Assembler {
        
        private int address = 0x100;
        private int pass = 1;
        private string binary = "";

        private List<string> source = new List<string>();
        private List<Symbol> table = new List<Symbol>();

        //Assembles a file into an output file
        //This can be standard assembly, or as an application
        public int Assemble(string fin, string fout, bool isApp) {
            int error = 0;
            int currentLine = 0;
            source.Clear();
            table.Clear();

            //If being assembled as an app, start assembly at 0x100 to leave space for stack
            address = isApp ? 0x100 : 0;

            //Reads in the file into the source list
            string line;
            StreamReader finReader = new StreamReader(fin);
            while ((line = finReader.ReadLine()) != null) {
                source.Add(line);
            }
            
            //Indicate assembly start, and initiate first pass
            Console.WriteLine("STARTING " + ((isApp) ? "APPLICATION " : "") + "ASSEMBLY");
            Console.WriteLine("PASS #1");
            
            //Pass #1
            pass = 1;
            while (currentLine != source.Count && error == 0) {
                error = ProcessLine(source[currentLine]);
                if (error > 0) Console.WriteLine("HALTED ON LINE " + (currentLine+1) + ", ERROR 0X" + DecToHex(error, 2));
                currentLine++;
            }
            
            if (error > 0) return error;

            //Pass #2
            Console.WriteLine("PASS #2");
            
            //Prepare binary
            binary = "";
		
            //Fail if the assembly is so large that it can't fit inside the 8080's address space
            if (address > 0xFFFF-0x100) {Console.WriteLine("ASSEMBLY TOO LONG"); return 0x5A;}
		
            //The first thing into the binary is the amount of blocks that MICRON needs to allocate in order to read in the program
            int blocksLong = (int) Math.Floor((address * 1.0) / 256);
            WriteByte(blocksLong, false);
		
            Console.WriteLine("FINAL ADDRESS 0X" + DecToHex(address, 4));
            Console.WriteLine("BINARY " + blocksLong + " BLOCK(S) LONG");
		
            //If being assembled as an app, start assembly at 0x100 to leave space for stack
            address = isApp ? 0x100 : 0;
            
            pass = 2;
            currentLine = 0;
            while (currentLine != source.Count && error == 0) {
                error = ProcessLine(source[currentLine]);
                if (error > 0) Console.WriteLine("ERROR " + DecToHex(error, 2) + " ON LINE " + (currentLine + 1));
                currentLine++;
            }
            
            
            //Write in end command
            binary = binary + DecToAscii(27) + DecToAscii(2);
            if (error > 0) return error;
            
            
            
            return error;
        }
        
        //Processes a line of instruction
        private int ProcessLine(string line) {
            int i = 0;
		
            bool isLabel = true;
	
            while (i != line.Length) {
			
                //Check if char is "#", indicates comment so line should be ignored
                if (line[i] == 35) {
                    break;
                }
			
                //If a line has whitespace before any actual characters, it will be treated as an instruction, otherwise it is a label
                if (line[i] < 33) {
                    isLabel = false;
                } else {
                    string[] args = ParseLine(line, i);
                    if (args.Length == 0) return 0;
                    if (isLabel) {
                        //Symbols only need to be inserted on the first pass
                        if (pass == 1) return RegisterSymbol(args[0], 2, true, address);
                        else return 0;
                    } else {
                        return ProcessInstruction(args);
                    }
                }
                i++;
            }
		
            return 0;
        }

        //Function to process actual instructions
        //On the first pass, only data spacing is handled in order to fill out the symbol table
        //On the second pass, actual instruction data is processed
        private int ProcessInstruction(string[] args) {
            
		//DEFINE SYMBOL Instruction: Manually insert a symbol into the symbol table
		if (args[0].Equals(".DEF")) {
			//We only define symbols on the first pass
			if (pass == 1) {
				if (args.Length < 3) return 0x53;
				Numeric n = ParseNumeric(args[2]);
				
				//Bad numeric, return error
				if (n == null) return 0x52;
				
				RegisterSymbol(args[1], n.Type, n.Relocatable, n.Value);
			}
		} else 
			
			//DEFINE BYTE Instruction: Manually insert data into the executable
		if (args[0].Equals(".DB")) {
			int i = 1;
			while (i != args.Length) {
				if (args[i][0] == '"' && args[i].Length > 2) {
					int o = 1;
					while (o != args[i].Length - 1) {
						//Increment address for every byte of the string during the first pass
						address++;
						if (pass == 2) {
							WriteByte(args[i][o], false);
						}
						o++;
					}
				} else {
					Numeric n = ParseNumeric(args[i]); 
					//Bad numeric, return error
					if (n == null) return 0x52;
					
					//Add the length of the numeric to the address during the first pass

					address = address + n.Type;
					if (pass == 2) {
						if (n.Type == 1) {
							WriteByte(n.Value, n.Relocatable);
						} else {
							WriteAddress(n.Value, n.Relocatable);
						}
					}
				}
				i++;
			}
		} else
			
		// CHANGE ORIGIN Instruction: Manually set the current address of the assembly, can only be addresses that are higher than current address
		if (args[0].Equals(".ORG")) {
			if (args.Length < 2) return 0x53;
			Numeric n = ParseNumeric(args[1]);
			
			//Bad numeric, return error
			if (n == null) return 0x52;
			
			//Return error if numeric is less than address
			if (address > n.Value) return 0x55;
			
			//Pad buffer with "0"
			int diff = n.Value - address;
			int i = 0;
			while (i != diff && pass == 2) {
				WriteByte(0, false);
				i++;
			}
			
			address = n.Value;
		} else
			
			//LD: Moves a register or value into another register
		if (args[0].Equals("LD")) {
			if (args.Length < 3) return 0x53;
			
			string dest = args[1];
			string src = args[2];
			int mpos = GetMainRegPos(src);
			int dpos = GetMainRegPos(dest);
			
			//The "A" register has extra options, this branch handles them
			if (dest.Equals("A")) {
				if (mpos != -1) {
					address++;
					if (pass == 2) WriteByte(0x78 + mpos, false);
				} else if (src.Equals("(BC)")) {
					address++;
					if (pass == 2) WriteByte(0x0A, false);
				} else if (src.Equals("(DE)")) {
					address++;
					if (pass == 2) WriteByte(0x1A, false);
				} else {
					string strip = StripPointer(src);
					//If strip is null, that means that the source is not a pointer, and to handle it like a constant
					if (strip == null) { 
						Numeric n = ParseNumeric(src);
						address = address + 2;
						
						//Bad numeric, return error if on second pass (due to possible later defined symbol)
						if (n == null) { if (pass == 2) return 0x52; else return 0; }
						
						//Value too large, return error
						if (n.Type > 1) return 0x57;
						
						if (pass == 2) {
							WriteByte(0x3E, false);
							WriteByte(n.Value, n.Relocatable);
						}
					} else {
						Numeric n = ParseNumeric(strip);
						address = address + 3;
						
						//Bad numeric, return error if on second pass (due to possible later defined symbol)
						if (n == null) { if (pass == 2) return 0x52; else return 0; }
						
						if (pass == 2) {
							WriteByte(0x3A, false);
							WriteAddress(n.Value, n.Relocatable);
						}
					}
				}
				// Otherwise, if the destination is a valid "standard" register that is not "A", this branch will be used
			} else if (dpos != -1) {
				//If both the source and destination are "standard" registers, then the following will be a 1 byte instruction
				if (mpos != -1) { 
					address++; 
					if (pass == 2) WriteByte(0x40 + ((dpos * 8) + mpos), false);
				//Otherwise, a constant is assumed
				} else {
					Numeric n = ParseNumeric(src);
					address = address + 2;
					
					//Bad numeric, return error if on second pass (due to possible later defined symbol)
					if (n == null) { if (pass == 2) return 0x52; else return 0; }
					
					//Value too large, return error
					if (n.Type > 1) return 0x57;
					
					if (pass == 2) {
						WriteByte(0x06 + (dpos * 8), false);
						WriteByte(n.Value, n.Relocatable);
					}

				}
			
				//For the register pairs "BC", "DE", the only options are to load a constant on the 8080
			} else if (dest.Equals("BC")) {
				Numeric n = ParseNumeric(src);
				address = address + 3;
				
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52; else return 0; }
				
				if (pass == 2) {
					WriteByte(0x01, false);
					WriteAddress(n.Value, n.Relocatable);
				}
				
				
			} else if (dest.Equals("DE")) {
				Numeric n = ParseNumeric(src);
				address = address + 3;
				
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52; else return 0; }
				
				if (pass == 2) {
					WriteByte(0x11, false);
					WriteAddress(n.Value, n.Relocatable);
				}
			//In addition to loading from a constant, the "HL" register can also load from a constant pointer
			} else if (dest.Equals("HL")) {
				string strip = StripPointer(src);
				
				//If strip is null, that means that the source is not a pointer, and to handle it like a constant
				if (strip == null) { 
					Numeric n = ParseNumeric(src);
					address = address + 3;
					
					//Bad numeric, return error if on second pass (due to possible later defined symbol)
					if (n == null) { if (pass == 2) return 0x52;
					else return 0; }
					
					if (pass == 2) {
						WriteByte(0x21, false);
						WriteAddress(n.Value, n.Relocatable);
					}

					
				} else {
					Numeric n = ParseNumeric(strip);
					address = address + 3;
					
					//Bad numeric, return error if on second pass (due to possible later defined symbol)
					if (n == null) { if (pass == 2) return 0x52; else return 0; }
					
					if (pass == 2) {
						WriteByte(0x2A, false);
						WriteAddress(n.Value, n.Relocatable);
					}
					
					
				}
				
				//The "SP" register can be set with either a constant, or loaded from "HL"
			} else if (dest.Equals("SP")) {
				if (src.Equals("HL")) {
					address = address + 1;
					if (pass == 2) WriteByte(0xF9, false);
				} else {
					Numeric n = ParseNumeric(src);
					address = address + 3;
					
					//Bad numeric, return error if on second pass (due to possible later defined symbol)
					if (n == null) { if (pass == 2) return 0x52; else return 0; }
					
					if (pass == 2) {
						WriteByte(0x31, false);
						WriteAddress(n.Value, n.Relocatable);
					}
					
	
				}
				
				//Both "(BC)" and "(DE)" can only be loaded from the "A" register
			} else if (dest.Equals("(BC)")) {
				if (src.Equals("A")) {
					address = address + 1;
					if (pass == 2) WriteByte(0x02, false);
				} else {
					return 0x56;
				}
			} else if (dest.Equals("(DE)")) {
				if (src.Equals("A")) {
					address = address + 1;
					if (pass == 2) WriteByte(0x12, false);
				} else {
					return 0x56;
				}
			}
			//If the destination is not a constant register or register/pointer, then the only other possibility is that it is a pointer
			else {
				string strip = StripPointer(src);
				
				//If strip is null, that means that the destination is not a pointer, and there for invalid
				if (strip == null) { 
					return 0x56;
				} else {
					Numeric n = ParseNumeric(strip);
					address = address + 3;
					
					//Bad numeric, return error if on second pass (due to possible later defined symbol)
					if (n == null) { if (pass == 2) return 0x52; else return 0; }
					
					if (pass == 2) {
						WriteByte(0x32, false);
						WriteAddress(n.Value, n.Relocatable);
					}
					
				}
			}
		} else
			
			//ADD: Add a register or value to another
		if (args[0].Equals("ADD")) {
			
			//If the instruction only has one argument, it is an addition to the "A" register
			if (args.Length == 2) {
				int dpos = GetMainRegPos(args[1]);
				if (dpos != -1) {
					address = address + 1;
					if (pass == 2) WriteByte(0x80 + dpos, false);
				//This situation occurs when a constant is added to the "A" register
				} else {
					Numeric n = ParseNumeric(args[1]);
					address = address + 2;
					
					//Bad numeric, return error if on second pass (due to possible later defined symbol)
					if (n == null) { if (pass == 2) return 0x52; else return 0; } 
					
					//Value too large, return error
					if (n.Type > 1) return 0x57;
					
					if (pass == 2) {
						WriteByte(0xC6, false);
						WriteByte(n.Value, n.Relocatable);
					}
				}
			} else if (args.Length > 2) {
				if (args[1].Equals("HL")) {
					//The only possible additions to "HL" are the other register pairs, these are all 1 byte instructions
					address = address + 1;
					if (args[2].Equals("BC")) {
						if (pass == 2) WriteByte(0x09, false);
					} else if (args[2].Equals("DE")) {
						if (pass == 2) WriteByte(0x19, false);
					} else if (args[2].Equals("HL")) {
						if (pass == 2) WriteByte(0x29, false);
					} else if (args[2].Equals("SP")) {
						if (pass == 2) WriteByte(0x39, false);
					} else {
						return 0x56;
					}
				} else {
					return 0x56;
				}
			} else {
				return 0x53;
			}
		} else
		
			//SUB: Subtract a register or value from "A"
		if (args[0].Equals("SUB")) {
			if (args.Length < 2) return 0x53;
			
			int dpos = GetMainRegPos(args[1]);
			if (dpos != -1) {
				//Standard main register subtraction
				address = address + 1;
				if (pass == 2) WriteByte(0x90 + dpos, false);
				
			//This situation occurs when a constant is subtracted from the "A" register
			} else {
				Numeric n = ParseNumeric(args[1]);
				address = address + 2;
				
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52; else return 0; }
				
				//Value too large, return error
				if (n.Type > 1) return 0x57;
				
				if (pass == 2) {
					WriteByte(0xD6, false);
					WriteByte(n.Value, n.Relocatable);
				}
			}
		} else
		
			//ADC: Add a register or value to "A" with carry
		if (args[0].Equals("ADC")) {
			if (args.Length < 2) return 0x53;
				
			int dpos = GetMainRegPos(args[1]);
			if (dpos != -1) {
				//Standard main register addition
				address = address + 1;
				if (pass == 2) WriteByte(0x88 + dpos, false);
				
			//This situation occurs when a constant is added to the "A" register with carry
			} else {
				Numeric n = ParseNumeric(args[1]);
				address = address + 2;
					
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52; else return 0; }
				
				//Value too large, return error
				if (n.Type > 1) return 0x57;
				
				if (pass == 2) {
					WriteByte(0xCE, false);
					WriteByte(n.Value, n.Relocatable);
				}
			}
		} else
			
			//SBC: Subtract a register or value from "A" with carry
		if (args[0].Equals("SBC")) {
			if (args.Length < 2) return 0x53;
						
			int dpos = GetMainRegPos(args[1]);
			if (dpos != -1) {
				//Standard main register subtraction
				address = address + 1;
				if (pass == 2) WriteByte(0x98 + dpos, false);
							
				//This situation occurs when a constant is subtracted from the "A" register with carry
			} else {
				Numeric n = ParseNumeric(args[1]);
				address = address + 2;
						
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52;
					else return 0; }
				
				//Value too large, return error
				if (n.Type > 1) return 0x57;
				
				if (pass == 2) {
					WriteByte(0xDE, false);
					WriteByte(n.Value, n.Relocatable);
				}
			}
		} else
		
			//AND: Perform an "AND" operation on a register or value with "A"
		if (args[0].Equals("AND")) {
			if (args.Length < 2) return 0x53;
							
			int dpos = GetMainRegPos(args[1]);
			if (dpos != -1) {
				//Standard main register subtraction
				address = address + 1;
				if (pass == 2) WriteByte(0xA0 + dpos, false);
								
				//This situation occurs when a constant is subtracted from the "A" register with carry
			} else {
				Numeric n = ParseNumeric(args[1]);
				address = address + 2;
							
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52;
					else return 0; }
				
				//Value too large, return error
				if (n.Type > 1) return 0x57;
					
				if (pass == 2) {
					WriteByte(0xE6, false);
					WriteByte(n.Value, n.Relocatable);
				}
			}
		} else
		
			//XOR: Perform an "XOR" operation on a register or value with "A"
		if (args[0].Equals("XOR")) {
			if (args.Length < 2) return 0x53;
							
			int dpos = GetMainRegPos(args[1]);
			if (dpos != -1) {
				//Standard main register subtraction
				address = address + 1;
				if (pass == 2) WriteByte(0xA8 + dpos, false);
								
				//This situation occurs when a constant is subtracted from the "A" register with carry
			} else {
				Numeric n = ParseNumeric(args[1]);
				address = address + 2;
							
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52;
					else return 0; }
				
				//Value too large, return error
				if (n.Type > 1) return 0x57;
					
				if (pass == 2) {
					WriteByte(0xEE, false);
					WriteByte(n.Value, n.Relocatable);
				}
			}
		} else 
		
			//OR: Perform an "OR" operation on a register or value with "A"
		if (args[0].Equals("OR")) {
			if (args.Length < 2) return 0x53;
							
			int dpos = GetMainRegPos(args[1]);
			if (dpos != -1) {
				//Standard main register subtraction
				address = address + 1;
				if (pass == 2) WriteByte(0xB0 + dpos, false);
								
				//This situation occurs when a constant is subtracted from the "A" register with carry
			} else {
				Numeric n = ParseNumeric(args[1]);
				address = address + 2;
							
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52;
					else return 0; }
				
				//Value too large, return error
				if (n.Type > 1) return 0x57;
					
				if (pass == 2) {
					WriteByte(0xF6, false);
					WriteByte(n.Value, n.Relocatable);
				}
			}
		} else
		
			//CP: Perform a "CP" operation on a register or value with "A"
		if (args[0].Equals("CP")) {
			if (args.Length < 2) return 0x53;
							
			int dpos = GetMainRegPos(args[1]);
			if (dpos != -1) {
				//Standard main register subtraction
				address = address + 1;
				if (pass == 2) WriteByte(0xB8 + dpos, false);
								
				//This situation occurs when a constant is subtracted from the "A" register with carry
			} else {
				Numeric n = ParseNumeric(args[1]);
				address = address + 2;
							
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52;
					else return 0; }
				
				//Value too large, return error
				if (n.Type > 1) return 0x57;
					
				if (pass == 2) {
					WriteByte(0xFE, false);
					WriteByte(n.Value, n.Relocatable);
				}
			}
		} else
		
			//RET: Pop the top of the stack into the "PC" register (return from a subroutine)
		if (args[0].Equals("RET")) {
			//All forms of the RET instruction take up 1 byte
			address++;
			
			//If the argument length is above 1, it means there is a conditional to process
			if (args.Length > 1) {
				
				//Get conditional, fail if invalid
				int cpos = GetConditionPos(args[1]);
				if (cpos == -1) return 0x58;
				
				if (pass == 2) WriteByte(0xC0 + (cpos * 8), false);
				
			//Otherwise, it is an unconditional return
			} else {
				if (pass == 2) WriteByte(0xC9, false);
			}
		} else
		
			//JP: Write a value to the "PC" register
		if (args[0].Equals("JP")) {
			
			//A 3 argument long jump indicates that it will be a conditional jump
			if (args.Length > 2) {
				address = address + 3;
				
				//Get conditional, fail if invalid
				int cpos = GetConditionPos(args[1]);
				if (cpos == -1) return 0x58;
				
				Numeric n = ParseNumeric(args[2]);
				
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52;
					else return 0; }
				
				if (pass == 2) {
					WriteByte(0xC2 + (cpos * 8), false);
					WriteAddress(n.Value, n.Relocatable);
				}
				
				
			} else if (args.Length == 2) {
				//If the argument is "(HL)", it will be an unconditional jump to the location pointed to by "HL", this is a 1 byte instruction
				if (args[1].Equals("(HL)")) {
					address++;
					if (pass == 2) WriteByte(0xE9, false);
					
				//Otherwise, it is an unconditional jump to a constant location
				} else {
					Numeric n = ParseNumeric(args[1]);	
					address = address + 3;
					
					//Bad numeric, return error if on second pass (due to possible later defined symbol)
					if (n == null) { if (pass == 2) return 0x52;
						else return 0; }
					
					if (pass == 2) {
						WriteByte(0xC3, false);
						WriteAddress(n.Value, n.Relocatable);
					}
				}
			} else {
				return 0x53;
			}
		} else
		
			//CALL: Write a value to the "PC" register, then push the old "PC" value onto the stack (subroutine call)
		if (args[0].Equals("CALL")) {
			
			//A 3 argument long jump indicates that it will be a conditional jump
			if (args.Length > 2) {
				address = address + 3;
				
				//Get conditional, fail if invalid
				int cpos = GetConditionPos(args[1]);
				if (cpos == -1) return 0x58;
				
				Numeric n = ParseNumeric(args[2]);
				
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52;
					else return 0; }
				
				if (pass == 2) {
					WriteByte(0xC4 + (cpos * 8), false);
					WriteAddress(n.Value, n.Relocatable);
				}
				
				
			} else if (args.Length == 2) {
				
				Numeric n = ParseNumeric(args[1]);	
				address = address + 3;
					
				//Bad numeric, return error if on second pass (due to possible later defined symbol)
				if (n == null) { if (pass == 2) return 0x52;
					else return 0; }
					
				if (pass == 2) {
					WriteByte(0xCD, false);
					WriteAddress(n.Value, n.Relocatable);
					
				}
			} else {
				return 0x53;
			}
		} else
		
			//PUSH: Push a value onto the stack
		if (args[0].Equals("PUSH")) {
			address++;
			if (args.Length < 2) return 0x53;
			
			//Pass #1, do not write
			if (pass == 1) return 0;
			
			//Find register to push
			if (args[1].Equals("BC")) {
				WriteByte(0xC5, false);
			} else if (args[1].Equals("DE")) {
				WriteByte(0xD5, false);
			} else if (args[1].Equals("HL")) {
				WriteByte(0xE5, false);
			} else if (args[1].Equals("AF")) {
				WriteByte(0xF5, false);
			} else {
				return 0x56;
			}
		} else
		
			//POP: Pop a value from the stack
		if (args[0].Equals("POP")) {
			address++;
			if (args.Length < 2) return 0x53;
			
			//Pass #1, do not write
			if (pass == 1) return 0;
			
			//Find register to pop into
			if (args[1].Equals("BC")) {
				WriteByte(0xC1, false);
			} else if (args[1].Equals("DE")) {
				WriteByte(0xD1, false);
			} else if (args[1].Equals("HL")) {
				WriteByte(0xE1, false);
			} else if (args[1].Equals("AF")) {
				WriteByte(0xF1, false);
			} else {
				return 0x56;
			}
		} else
		
			//OUT: Write register "A" to a specified port
		if (args[0].Equals("OUT")) {
			if (args.Length < 2) return 0x53;
			address = address + 2;
			
			Numeric n = ParseNumeric(args[1]);	
			
			//Bad numeric, return error if on second pass (due to possible later defined symbol)
			if (n == null) { if (pass == 2) return 0x52; else return 0; }
			
			//Value too large, return error
			if (n.Type > 1) return 0x57;
			
			if (pass == 2) {
				WriteByte(0xD3, false);
				WriteByte(n.Value, n.Relocatable);
			}
		} else 
		
			//IN: Read specified port into register "A"
		if (args[0].Equals("IN")) {
			if (args.Length < 2) return 0x53;
			address = address + 2;
			
			Numeric n = ParseNumeric(args[1]);	
			
			//Bad numeric, return error if on second pass (due to possible later defined symbol)
			if (n == null) { if (pass == 2) return 0x52; else return 0; }
			
			//Value too large, return error
			if (n.Type > 1) return 0x57;
			
			if (pass == 2) {
				WriteByte(0xDB, false);
				WriteByte(n.Value, n.Relocatable);
			}
		} else
		
			//RST: Jump to location in memory, push "PC"+1 onto the stack
		if (args[0].Equals("RST")) {
			if (args.Length < 2) return 0x53;
			address++;
			
			Numeric n = ParseNumeric(args[1]);	
			
			//Bad numeric, return error if on second pass (due to possible later defined symbol)
			if (n == null) { if (pass == 2) return 0x52;else return 0; }
			
			//Value too large, return error
			if (n.Type > 1) return 0x57;
			
			//Value must be divisible by 8
			if (n.Value % 8 == 0 || n.Value > 0x38) return 0x59;
			
			if (pass == 2) WriteByte(0xC7 + n.Value, false);
		} else
		
			//EI: Enable interrupts
		if (args[0].Equals("EI")) {
			address++;
			if (pass == 2) WriteByte(0xFB, false);
		} else
		
			//DI: Disable interrupts
		if (args[0].Equals("DI")) {
			address++;
			if (pass == 2) WriteByte(0xF3, false);
		} else
		
			//EX: Exchange register pairs
		if (args[0].Equals("EX")) {
			if (args.Length < 3) return 0x53;
			address++;
			
			//Only two options, "(SP)" and "HL", or "DE" and "HL"
			if (args[1].Equals("(SP)") && args[2].Equals("HL")) {
				if (pass == 2) WriteByte(0xE3, false);
			} else if (args[1].Equals("DE") && args[2].Equals("HL")) {
				if (pass == 2) WriteByte(0xEB, false);
			} else {
				return 0x56;
			}
		} else
		
			//INC: Increment a register or register pair
		if (args[0].Equals("INC")) {
			if (args.Length < 2) return 0x53;
			int dpos = GetMainRegPos(args[1]);
			
			address++;
			
			//If the destination position isn't -1, then it is a main register, otherwise it is a register pair
			if (dpos != -1) {
				 if (pass == 2) WriteByte(0x04 + (dpos * 8), false);
			} else if (args[1].Equals("BC")) {
				if (pass == 2) WriteByte(0x03, false);
			} else if (args[1].Equals("DE")) {
				if (pass == 2) WriteByte(0x13, false);
			} else if (args[1].Equals("HL")) {
				if (pass == 2) WriteByte(0x23, false);
			} else if (args[1].Equals("SP")) {
				if (pass == 2) WriteByte(0x33, false);
			} else {
				return 0x56;
			}
		} else
		
			//DEC: Decrement a register or register pair
		if (args[0].Equals("DEC")) {
			if (args.Length < 2) return 0x53;
			int dpos = GetMainRegPos(args[1]);
			
			address++;
			
			//If the destination position isn't -1, then it is a main register, otherwise it is a register pair
			if (dpos != -1) {
				 if (pass == 2) WriteByte(0x05 + (dpos * 8), false);
			} else if (args[1].Equals("BC")) {
				if (pass == 2) WriteByte(0x0B, false);
			} else if (args[1].Equals("DE")) {
				if (pass == 2) WriteByte(0x1B, false);
			} else if (args[1].Equals("HL")) {
				if (pass == 2) WriteByte(0x2B, false);
			} else if (args[1].Equals("SP")) {
				if (pass == 2) WriteByte(0x3B, false);
			} else {
				return 0x56;
			}
		} else
		
			//RLCA: Rotate "A" left, and copy bit 7 to bit 0 + carry
		if (args[0].Equals("RLCA")) {
			address++;
			if (pass == 2) WriteByte(0x07, false);
		} else
		
			//RLA: Rotate "A" left, the carry flag is copied to bit 0, and bit 7 is copied to the carry flag
		if (args[0].Equals("RLA")) {
			address++;
			if (pass == 2) WriteByte(0x17, false);
		} else
		
			//DAA: Adjust for BCD addition and subtraction
		if (args[0].Equals("DAA")) {
			address++;
			if (pass == 2) WriteByte(0x27, false);
		} else
		
			//SCF: Set carry flag
		if (args[0].Equals("SCF")) {
			address++;
			if (pass == 2) WriteByte(0x37, false);
		} else

			//RLCA: Rotate "A" right, and copy bit 7 to bit 0 + carry
		if (args[0].Equals("RRCA")) {
			address++;
			if (pass == 2) WriteByte(0x0F, false);
		} else
		
			//RLA: Rotate "A" right, the carry flag is copied to bit 0, and bit 7 is copied to the carry flag
		if (args[0].Equals("RRA")) {
			address++;
			if (pass == 2) WriteByte(0x1F, false);
		} else
		
			//CPL: Contents of "A" are inverted
		if (args[0].Equals("CPL")) {
			address++;
			if (pass == 2) WriteByte(0x2F, false);
		} else
		
			//CCF: Invert carry flag carry flag
		if (args[0].Equals("CCF")) {
			address++;
			if (pass == 2) WriteByte(0x3F, false);
		} else
		
			//OS: Do an OS call
		if (args[0].Equals("OS")) {
			address = address + 3;
			if (pass == 2) {
				WriteByte(0xCD, false);
				WriteCall();
			}
		}
		
		// Unidentified Instruction Handling
		else {
			Console.WriteLine("UNID INSTR: ");
			int o = 0;
			while (o != args.Length) {
				Console.WriteLine(args[o] + " ");
				o++;
			}
			Console.WriteLine("");
			return 0x54;
		}
		return 0;
        }
        
        //Returns the position of a register
        private int GetMainRegPos(string reg) {
	        switch (reg) {
		        case "B": return 0;
		        case "C": return 1;
		        case "D": return 2;
		        case "E": return 3;
		        case "H": return 4;
		        case "L": return 5;
		        case "(HL)": return 6;
		        case "A": return 7;
		        default: return -1;
	        }
        }
	
        //Returns the position of a flag
        private int GetConditionPos(string reg) {
	        switch (reg) {
		        case "NZ": return 0;
		        case "Z": return 1;
		        case "NC": return 2;
		        case "C": return 3;
		        case "PO": return 4;
		        case "PE": return 5;
		        case "P": return 6;
		        case "M": return 7;
		        default: return -1;
	        }
        }
        
        //Writes an address to the binary
        private void WriteAddress(int s, bool relocated) {
	        //Debugging outputs
		
	        /*System.out.print("WRITTEN: 0X" + decToHex(s,4));
	        if (relocated) System.out.println("*");
	        else System.out.println(" ");*/
		
		
	        //Quick and dirty, there are better ways but I don't feel like testing them
	        String hex = DecToHex(s,4);
		
	        String highByte = hex.Substring(0, 2);
	        String lowByte = hex.Substring(2, 4);
		
	        int lowInt = HexToDec(lowByte);
		
	        if (lowInt == 27) binary = binary + DecToAscii(27);
	        binary = binary + DecToAscii(lowInt);
	        //If relocated, write the relocate escape command
		
	        if (relocated) binary = binary + DecToAscii(27) + DecToAscii(0);
	        binary = binary + DecToAscii(HexToDec(highByte));
		
	        //Implement escape for loader
	        if (HexToDec(highByte) == 27 && !relocated) binary = binary + DecToAscii(27);
        }
        
        //Converts a simple integer, hexadecimal, or symbol into a numeric
        private Numeric ConvertNumeric(string num) {
	        int value = 0;
	        int type = 1;
	        bool relocatable = false;
	        if (IsInteger(num)) {
		        int x = int.Parse(num);
			 
		        if (x > 255) {
			        type = 2;
		        }
		        while (x > 65535) {
			        x = x - 65536;
			        type = 2;
		        }
			 
		        value = x;
			 
			 
	        } else if (IsHex(num)) {
		        if (num.Length == 4) {
			        type = 1;
			        value = HexToDec(num.Substring(2, 4));
		        } else if (num.Length == 6) {
			        type = 2;
			        value = HexToDec(num.Substring(2,6));
		        } else {
			        //Shouldn't ever be able to get here
			        return null;
		        }
	        } else if (SymbolExists(num)) {
		        Symbol sym = GetSymbol(num);
		        value = sym.Value;
		        type = sym.Type;
		        relocatable = sym.Relocatable;
			 
	        } else return null;
		
	        return new Numeric(value, type, relocatable);
        }

        private bool IsInteger(String str) {
	        return int.TryParse(str, out _);
        }
        
        private bool IsHex(string test) {
	        // For C-style hex notation (0xFF) you can use @"\A\b(0[xX])?[0-9a-fA-F]+\b\Z"
	        return System.Text.RegularExpressions.Regex.IsMatch(test, @"\A\b[0-9a-fA-F]+\b\Z");
        }

        private int HexToDec(string hex) {
	        return Convert.ToInt32(hex, 16);
        }

        //Retrieves a symbol from the table
        private Symbol GetSymbol(String sym) {
	        int i = 0;
	        while (i != table.Count) {
		        if (table[i].Name.Equals(sym)) return table[i];
		        i++;
	        }
	        return null;
        }

        //Parses a numeric phrase into a numeric object, with a value, length, and relocation flag
        //Accounts for addition and subtraction operators
        private Numeric ParseNumeric(string phrase) {
	        int value = 0;
	        int type = 1;
		
	        String buffer = "";
	        int sign = 1;
		
	        bool relocatable = false;
		
	        int i = 0;
	        while (i != phrase.Length) {
		        if (phrase[i] == '*') {
			        relocatable = true;
			        i++;
			        continue;
		        }
		        if (phrase[i] == '+' || phrase[i] == '-') {
			        if (phrase[i] == '+') {
				        sign = 1;
			        } else sign = -1;
			
			        Numeric n = ConvertNumeric(buffer);
			        if (n == null) return null;
				 
			        if (n.Relocatable) relocatable = true;
			        if (n.Type == 2) {
				        type = 2;
			        }
			        value = value + (sign * n.Value);
			        while (type == 1 && value > 255) {
				        value = value - 256;
			        }
			        while (type == 1 && value < 0) {
				        value = value + 256;
			        }
			        while (type == 2 && value > 65535) {
				        value = value - 65536;
			        }
			        while (type == 2 && value < 0) {
				        value = value + 65536;
			        }
				
			        buffer = "";
		        } else {
			        buffer = buffer + phrase[i];
		        }
		        i++;
	        }	
	        if (buffer.Length > 0) {
		        Numeric n = ConvertNumeric(buffer);
		        if (n == null) return null;
			 
		        if (n.Relocatable) relocatable = true;
		        if (n.Type == 2) {
			        type = 2;
		        }
		        value = value + (sign * n.Value);
		        while (type == 1 && value > 255) {
			        value = value - 256;
		        }
		        while (type == 1 && value < 0) {
			        value = value + 256;
		        }
		        while (type == 2 && value > 65535) {
			        value = value - 65536;
		        }
		        while (type == 2 && value < 0) {
			        value = value + 65536;
		        }
	        }
	        return new Numeric(value, type, relocatable);
        }
        
        //Registers a new symbol into the table
        private int RegisterSymbol(string sym, int type, bool relocatable, int value) {
            if (SymbolExists(sym)) return 0x51;
            table.Add(new Symbol(sym, type, relocatable, value));
		
            //Print New Symbol
            Console.WriteLine(relocatable ? "*" : " ");
            if (type == 2) {
                Console.WriteLine("0X" + DecToHex(value, 4) + " " + sym);
            } else {
                Console.WriteLine("0X" + DecToHex(value, 2) + "   " + sym);
            }
		
            return 0;
        }
        
        //Checks if symbol exists
        private bool SymbolExists(string sym) {
            //Cycle through table
            foreach (Symbol s in table) {
                if (s.Name.Equals(sym)) return true;
            }
            return false;
        }
        
        private void WriteCall() { 
	        binary = binary + DecToAscii(27) + DecToAscii(1);
        }
        
        //Decimal to hexadecimal
        private string DecToHex(int i, int l) {
            string output = i.ToString("X");
            if (l != -1) {
                while (output.Length < l) {
                    output = "0" + output;
                }
                while (output.Length > l) {
                    output = output.Substring(1, output.Length);
                }
            }
            return output;
        }
        
        //Parses a line of text into an array, split up by " ", ",", and ":"
        private string[] ParseLine(string line, int startIndex) {
            List<string> buffer = new List<string>();
		
            string t = "";
            bool isString = false;
            
            while (startIndex != line.Length) {
                char c = line[startIndex];
                if ((c == 32 || c == 44 || c == 58) && !isString) {
                    if (t.Length > 0) buffer.Add(t);
                    t = "";
                } else {
                    t = t + c;
                    if (c == 34) isString = !isString;
                }
                startIndex++;
            }
		
            if (t.Length > 0) buffer.Add(t);
            if (buffer.Count > 0) return buffer.ToArray();
            else return null;
        }
        
        //Strips the pointer off of a string
        private String StripPointer(string inp) {
	        if (inp.Length > 2) {
		        if (inp[0] == '(' && inp[inp.Length - 1] == ')') {
			        return inp.Substring(1, inp.Length-1);
		        }
	        }
	        return null;
        }
        
        //Writes a byte into the binary, accounting for relocation
        private void WriteByte(int b, bool relocated) {
            while (b > 255) b = b - 255;
		
            //If relocated, write the relocate escape command
            if (relocated) binary = binary + DecToAscii(27) + DecToAscii(0);
		
            binary = binary + DecToAscii(b);
		
            //Implement escape for loader
            if (b == 27 && !relocated) binary = binary + DecToAscii(27);
        }
        
        //Decimal to ASCII character
        private string DecToAscii(int i) {
            return ((char) i) + "";
        }
    }
}