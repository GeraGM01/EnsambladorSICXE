using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace Practica02
{
    public class Pass2Visitor
    {
        private Dictionary<string, byte> opcodeTable = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            // Formato 1
            { "FIX",   0xC4 },
            { "FLOAT", 0xC0 },
            { "HIO",   0xF4 },
            { "NORM",  0xC8 },
            { "SIO",   0xF0 },
            { "TIO",   0xF8 },

            // Formato 2
            { "ADDR",   0x90 },
            { "SUBR",   0x94 },
            { "COMPR",  0xA0 },
            { "DIVR",   0x9C },
            { "MULR",   0x98 },
            { "RMO",    0xAC },
            { "SHIFTL", 0xA4 },
            { "SHIFTR", 0xA8 },
            { "SVC",    0xB0 },
            { "CLEAR",  0xB4 },
            { "TIXR",   0xB8 },

            // Formato 3/4
            { "ADD",    0x18 },
            { "ADDF",   0x58 },
            { "AND",    0x40 },
            { "COMP",   0x28 },
            { "COMPF",  0x88 },
            { "DIV",    0x24 },
            { "DIVF",   0x64 },
            { "J",      0x3C },
            { "JEQ",    0x30 },
            { "JGT",    0x34 },
            { "JLT",    0x38 },
            { "JSUB",   0x48 },
            { "LDA",    0x00 },
            { "LDB",    0x68 },
            { "LDCH",   0x50 },
            { "LDF",    0x70 },
            { "LDL",    0x08 },
            { "LDS",    0x6C },
            { "LDT",    0x74 },
            { "LDX",    0x04 },
            { "LPS",    0xD0 },
            { "MUL",    0x20 },
            { "MULF",   0x60 },
            { "OR",     0x44 },
            { "RD",     0xD8 },
            { "RSUB",   0x4C },
            { "SSK",    0xEC },
            { "STA",    0x0C },
            { "STB",    0x78 },
            { "STCH",   0x54 },
            { "STF",    0x80 },
            { "STI",    0xD4 },
            { "STL",    0x14 },
            { "STS",    0x7C },
            { "STSW",   0xE8 },
            { "STT",    0x84 },
            { "STX",    0x10 },
            { "SUB",    0x1C },
            { "SUBF",   0x5C },
            { "TD",     0xE0 },
            { "TIX",    0x2C },
            { "WD",     0xDC },
        };

        private Dictionary<string, int> registerNumber = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "A", 0 },
            { "X", 1 },
            { "L", 2 },
            { "B", 3 },
            { "S", 4 },
            { "T", 5 },
            { "F", 6 },
            { "PC", 8 },
            { "SW", 9 }
        };

        private List<int> wordModificationAddresses = new List<int>();
        private int baseAddress = -1;

        public List<string> ObjRecords { get; private set; } = new List<string>();

        public void Paso2(Pass1Visitor pass1)
        {
            var lines = pass1.Lines;
            var symtab = pass1.SymbolInfoTable;
            var blockTable = pass1.BlockTable;

            wordModificationAddresses.Clear();

            foreach (var ln in lines)
            {
                if (!string.IsNullOrEmpty(ln.Error))
                    continue;

                ln.ObjectCode = "";
                string op = ln.Mnemonic;
                if (string.IsNullOrEmpty(op))
                    continue;

                // Directivas sin objeto
                if (op.Equals("START", StringComparison.OrdinalIgnoreCase) ||
                    op.Equals("RESB", StringComparison.OrdinalIgnoreCase) ||
                    op.Equals("RESW", StringComparison.OrdinalIgnoreCase) ||
                    op.Equals("EQU", StringComparison.OrdinalIgnoreCase) ||
                    op.Equals("ORG", StringComparison.OrdinalIgnoreCase) ||
                    op.Equals("USE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // END
                if (op.Equals("END", StringComparison.OrdinalIgnoreCase))
                {
                    string endSymbol = ln.Operand?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(endSymbol))
                    {
                        if (!symtab.ContainsKey(endSymbol))
                        {
                            ln.Error = pass1.ConcatError(
                                ln.Error,
                                "Símbolo no encontrado en la directiva END"
                            );
                            ln.ObjectCode = "FFFFFF";
                        }
                    }
                    continue;
                }

                // BASE
                if (op.Equals("BASE", StringComparison.OrdinalIgnoreCase))
                {
                    string baseSymbol = ln.Operand?.Trim() ?? "";
                    if (!symtab.ContainsKey(baseSymbol))
                    {
                        ln.Error = pass1.ConcatError(
                            ln.Error,
                            "Símbolo no encontrado en TABSIM en (BASE)"
                        );
                    }
                    else
                    {
                        var si = symtab[baseSymbol];
                        baseAddress = si.Address + blockTable[si.BlockNumber].StartAddress;
                    }
                    continue;
                }

                // BYTE
                if (op.Equals("BYTE", StringComparison.OrdinalIgnoreCase))
                {
                    ln.ObjectCode = GenerateByteObject(ln.Operand, out string eByte);
                    if (!string.IsNullOrEmpty(eByte))
                    {
                        ln.Error = pass1.ConcatError(ln.Error, eByte);
                    }
                    continue;
                }

                // WORD
                if (op.Equals("WORD", StringComparison.OrdinalIgnoreCase))
                {
                    string operandToEvaluate =
                        string.IsNullOrEmpty(ln.OriginalExpression) ? ln.Operand : ln.OriginalExpression;

                    var evalRes = EvaluateExpressionPass2(operandToEvaluate, pass1, ln.Address);
                    if (evalRes.Type == ExprType.Error)
                    {
                        ln.Error = pass1.ConcatError(ln.Error, evalRes.ErrorMsg);
                    }
                    else
                    {
                        int val = evalRes.Value & 0xFFFFFF;
                        ln.ObjectCode = val.ToString("X6");
                        if (evalRes.Type == ExprType.Relative)
                        {
                            wordModificationAddresses.Add(ln.Address);
                            ln.IsRelocatable = true;
                        }
                    }
                    continue;
                }

                // Instrucciones
                bool extended = false;
                string bareMnemonic = op;
                if (op.StartsWith("+"))
                {
                    extended = true;
                    bareMnemonic = op.Substring(1);
                }

                if (!opcodeTable.TryGetValue(bareMnemonic, out byte baseOp))
                {
                    ln.Error = pass1.ConcatError(ln.Error, "Instrucción no encontrada en opcodeTable");
                    continue;
                }

                int lineAbsAddr = blockTable[ln.BlockNumber].StartAddress + ln.Address;

                // Formato 1
                if (ln.Format == "F1")
                {
                    ln.ObjectCode = baseOp.ToString("X2");
                }
                // Formato 2
                else if (ln.Format == "F2")
                {
                    byte r1r2 = ParseRegisterPair(ln.Operand, out string eF2, registerNumber);
                    if (!string.IsNullOrEmpty(eF2))
                    {
                        ln.Error = pass1.ConcatError(ln.Error, "Modo F2 error");
                        ln.ObjectCode = "";
                        continue;
                    }
                    ln.ObjectCode = baseOp.ToString("X2") + r1r2.ToString("X2");
                }
                else
                {
                    // (F3 / F4)
                    if (bareMnemonic.Equals("RSUB", StringComparison.OrdinalIgnoreCase))
                    {
                        byte rsubOp = (byte)((baseOp & 0xFC) | 0x03);
                        if (!extended)
                        {
                            ln.ObjectCode = rsubOp.ToString("X2") + "0000";
                        }
                        else
                        {
                            byte xbpe = 0x1;
                            ln.ObjectCode = rsubOp.ToString("X2") + xbpe.ToString("X2") + "00000";
                        }
                        continue;
                    }

                    bool n = false, iFlag = false, x = false;
                    string operand = ln.Operand?.Trim() ?? "";

                    // Detectar # o @
                    if (operand.StartsWith("#"))
                    {
                        n = false;
                        iFlag = true;
                        operand = operand.Substring(1).Trim();
                    }
                    else if (operand.StartsWith("@"))
                    {
                        n = true;
                        iFlag = false;
                        operand = operand.Substring(1).Trim();
                    }
                    else
                    {
                        n = true;
                        iFlag = true;
                    }

                    // Quitar paréntesis #(...) / @(...)
                    if (operand.StartsWith("(") && operand.EndsWith(")"))
                    {
                        string exprIn = operand.Substring(1, operand.Length - 2);
                        var evv = EvaluateExpressionPass2(exprIn, pass1, ln.Address);
                        if (evv.Type == ExprType.Error)
                        {
                            ln.Error = pass1.ConcatError(ln.Error, evv.ErrorMsg);
                            continue;
                        }
                        operand = evv.Value.ToString();
                    }

                    // Revisar si ,X
                    if (operand.EndsWith(",X", StringComparison.OrdinalIgnoreCase))
                    {
                        x = true;
                        operand = operand.Substring(0, operand.Length - 2).Trim();
                    }

                    // Evaluar si es símbolo
                    int target;
                    if (!int.TryParse(operand, out target))
                    {
                        var ev2 = EvaluateExpressionPass2(operand, pass1, ln.Address);
                        if (ev2.Type == ExprType.Error)
                        {
                            ln.Error = pass1.ConcatError(ln.Error, "Símb no hallado/Eval fail");
                            ln.ObjectCode = ForceErrorObject(0xFFF, 0xFFF, baseOp, !extended, ln, "");
                            continue;
                        }
                        target = ev2.Value;
                    }

                    byte opNi = (byte)((baseOp & 0xFC) | ((n ? 1 : 0) << 1) | (iFlag ? 1 : 0));
                    bool isF3 = !extended;

                    // *** Manejo especial #constante
                    if (iFlag && !n) // => #inmediato
                    {
                        // Checar si era un literal decimal
                        if (int.TryParse(operand, out int numericVal))
                        {
                            // si excede 12 bits => Forzar F4
                            if (numericVal < 0 || numericVal > 0xFFFFF)
                            {
                                ln.Error = pass1.ConcatError(ln.Error, "Valor inmediato fuera de rango (max 20 bits).");
                                ln.ObjectCode = ForceErrorObject(0xFFFFF, 0xFFFFF, opNi, false, ln, "");
                                continue;
                            }
                            if (numericVal <= 0xFFF && isF3)
                            {
                                // F3 “directo”
                                ln.ObjectCode = BuildF3ObjectCodeDirect(opNi, x, numericVal);
                                continue;
                            }
                            else
                            {
                                // F4
                                ln.ObjectCode = BuildF4ObjectCode(opNi, x, numericVal);
                                continue;
                            }
                        }
                    }

                    // Si no es #const => la lógica original
                    if (isF3)
                    {
                        int pcNext = lineAbsAddr + 3;
                        int disp = target - pcNext;
                        bool usePC = true;

                        if (disp < -2048 || disp > 2047)
                        {
                            if (baseAddress >= 0)
                            {
                                int dispB = target - baseAddress;
                                if (dispB < 0 || dispB > 4095)
                                {
                                    ln.Error = pass1.ConcatError(ln.Error, "Rango base normal");
                                    ln.ObjectCode = ForceErrorObject(0xFFF, 0xFFF, opNi, true, ln, "");
                                    continue;
                                }
                                else
                                {
                                    disp = dispB;
                                    usePC = false;
                                }
                            }
                            else
                            {
                                ln.Error = pass1.ConcatError(ln.Error, "No base y disp>PC");
                                ln.ObjectCode = ForceErrorObject(0xFFF, 0xFFF, opNi, true, ln, "");
                                continue;
                            }
                        }
                        ln.ObjectCode = BuildF3ObjectCode(opNi, x, disp, usePC);
                    }
                    else
                    {
                        // F4
                        if (target < 0 || target > 0xFFFFF)
                        {
                            ln.Error = pass1.ConcatError(ln.Error, "Rango F4");
                            ln.ObjectCode = ForceErrorObject(0xFFFFF, 0xFFFFF, opNi, false, ln, "");
                            continue;
                        }
                        ln.ObjectCode = BuildF4ObjectCode(opNi, x, target);
                    }
                }
            }
        }

        public void GenerarRegistrosHTME(Pass1Visitor pass1)
        {
            ObjRecords.Clear();
            var lines = pass1.Lines;
            var symtab = pass1.SymbolInfoTable;

            int startAddress = 0;
            int firstCodeAddr = -1;
            string programName = "NONAME";
            bool endHasError = false;

            foreach (var ln in lines)
            {
                if ((ln.Mnemonic ?? "").Equals("START", StringComparison.OrdinalIgnoreCase))
                {
                    startAddress = ln.Address;
                    if (!string.IsNullOrEmpty(ln.Label))
                        programName = ln.Label;
                    break;
                }
            }

            foreach (var ln in lines)
            {
                if (!string.IsNullOrEmpty(ln.ObjectCode))
                {
                    int absAddr = pass1.BlockTable[ln.BlockNumber].StartAddress + ln.Address;
                    firstCodeAddr = absAddr;
                    break;
                }
            }

            int endSymbolAddr = -1;
            foreach (var ln in lines)
            {
                if ((ln.Mnemonic ?? "").Equals("END", StringComparison.OrdinalIgnoreCase))
                {
                    if (ln.ObjectCode == "FFFFFF")
                    {
                        endHasError = true;
                        break;
                    }
                    string endOp = ln.Operand?.Trim();
                    if (!string.IsNullOrEmpty(endOp) && symtab.ContainsKey(endOp))
                    {
                        var sy = symtab[endOp];
                        int symAbs = pass1.BlockTable[sy.BlockNumber].StartAddress + sy.Address;
                        endSymbolAddr = symAbs;
                    }
                    break;
                }
            }

            int entryPoint;
            if (endHasError)
            {
                entryPoint = -1;
            }
            else if (endSymbolAddr >= 0)
                entryPoint = endSymbolAddr;
            else if (firstCodeAddr >= 0)
                entryPoint = firstCodeAddr;
            else
                entryPoint = startAddress;

            int progLength = pass1.FinalLocctr - startAddress;
            string name6 = (programName + "------").Substring(0, 6).ToUpper();
            string startHex = startAddress.ToString("X6");
            string lengthHex = progLength.ToString("X6");

            string headRec = $"H{name6}{startHex}{lengthHex}";
            ObjRecords.Add(headRec);

            StringBuilder currentObj = new StringBuilder();
            int currentStart = -1;
            int bytesInRecord = 0;
            const int maxBytes = 30;

            foreach (var ln in lines)
            {
                // Cierra T si es RESW/RESB
                if ((ln.Mnemonic ?? "").Equals("RESW", StringComparison.OrdinalIgnoreCase) ||
                    (ln.Mnemonic ?? "").Equals("RESB", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentObj.Length > 0 && currentStart >= 0)
                    {
                        EmitTextRecord(ObjRecords, currentStart, currentObj.ToString(), bytesInRecord);
                        currentObj.Clear();
                        currentStart = -1;
                        bytesInRecord = 0;
                    }
                    continue;
                }

                if (!string.IsNullOrEmpty(ln.ObjectCode))
                {
                    int absAddr = pass1.BlockTable[ln.BlockNumber].StartAddress + ln.Address;
                    int lengthBytes = ln.ObjectCode.Length / 2;
                    if (currentStart < 0)
                    {
                        currentStart = absAddr;
                        currentObj.Clear();
                        bytesInRecord = 0;
                    }
                    if (bytesInRecord + lengthBytes > maxBytes)
                    {
                        EmitTextRecord(ObjRecords, currentStart, currentObj.ToString(), bytesInRecord);
                        currentObj.Clear();
                        currentStart = absAddr;
                        bytesInRecord = 0;
                    }
                    currentObj.Append(ln.ObjectCode);
                    bytesInRecord += lengthBytes;
                }
            }

            if (currentObj.Length > 0 && currentStart >= 0)
            {
                EmitTextRecord(ObjRecords, currentStart, currentObj.ToString(), bytesInRecord);
            }

            // Registros M => F4
            foreach (var ln in lines)
            {
                if ((ln.Format ?? "") == "F4" && !string.IsNullOrEmpty(ln.ObjectCode))
                {
                    int modAddress = pass1.BlockTable[ln.BlockNumber].StartAddress + ln.Address;
                    string modRec = $"M{modAddress:X6}05+{programName.ToUpper()}";
                    ObjRecords.Add(modRec);
                }
            }

            // M => WORD reloc
            foreach (int waddr in wordModificationAddresses)
            {
                var l = pass1.Lines.FirstOrDefault(
                    x => x.Address == waddr &&
                         (x.Mnemonic ?? "").Equals("WORD", StringComparison.OrdinalIgnoreCase)
                );
                if (l != null)
                {
                    int absW = pass1.BlockTable[l.BlockNumber].StartAddress + l.Address;
                    string modRec = $"M{absW:X6}06+{programName.ToUpper()}";
                    ObjRecords.Add(modRec);
                }
            }

            if (endHasError)
            {
                ObjRecords.Add("EFFFFFF");
            }
            else
            {
                if (entryPoint < 0)
                    ObjRecords.Add("E000000");
                else
                    ObjRecords.Add($"E{entryPoint:X6}");
            }
        }

        public void DumpPaso2Table(Pass1Visitor pass1, string filePath)
        {
            using (var sw = new StreamWriter(filePath, false))
            {
                sw.WriteLine("=== TABLA PASO 2 ===");
                sw.WriteLine("CP      LABEL      MNEMONIC    OPERAND     FORM   ERROR                      OBJCODE");
                sw.WriteLine("------------------------------------------------------------------------------");
                foreach (var ln in pass1.Lines)
                {
                    string cpHex = ln.Address.ToString("X4");
                    string lbl = ln.Label ?? "";
                    string mne = ln.Mnemonic ?? "";

                    // Si es EQU/WORD => expresión original
                    string opn;
                    if ((mne.Equals("EQU", StringComparison.OrdinalIgnoreCase) ||
                         mne.Equals("WORD", StringComparison.OrdinalIgnoreCase)) &&
                        !string.IsNullOrEmpty(ln.OriginalExpression))
                    {
                        opn = ln.OriginalExpression;
                    }
                    else
                    {
                        opn = ln.Operand ?? "";
                    }

                    string fmt = ln.Format ?? "";
                    string err = ln.Error ?? "";
                    string obj = ln.ObjectCode ?? "";

                    if (ln.IsRelocatable && !string.IsNullOrEmpty(obj))
                    {
                        obj += "*";
                    }

                    sw.WriteLine($"{cpHex,-7} {lbl,-10} {mne,-10} {opn,-10} {fmt,-5} {err,-25} {obj}");
                }
                sw.WriteLine();
            }
        }

        public void DumpHTMEToFile(string filePath)
        {
            using (var sw = new StreamWriter(filePath, false))
            {
                sw.WriteLine("=== REGISTROS HTME ===");
                foreach (var rec in ObjRecords)
                {
                    sw.WriteLine(rec);
                }
                sw.WriteLine();
            }
        }

        // ======================================================
        //  Funciones extras para el "modo inmediato directo"
        // ======================================================
        private string BuildF3ObjectCodeDirect(byte opNi, bool x, int val)
        {
            byte flags = 0;
            if (x) flags |= 0x80;

            int disp12 = val & 0xFFF;
            byte first = (byte)(flags | ((disp12 >> 8) & 0x0F));
            byte second = (byte)(disp12 & 0xFF);
            return opNi.ToString("X2") + first.ToString("X2") + second.ToString("X2");
        }

        /// <summary>
        /// CORRECCIÓN PRINCIPAL:
        /// Sólo sumamos "StartAddress" si el símbolo es REL, NO si es ABS.
        /// </summary>
        private EvalResult EvaluateExpressionPass2(string expr, Pass1Visitor pass1, int currentLineAddress)
        {
            string transformed = expr;

            // Regex: tokens ID o "*"
            var pattern = @"\w+|\*";
            var matches = Regex.Matches(transformed, pattern);
            foreach (Match m in matches)
            {
                string tk = m.Value;
                if (tk == "*")
                {
                    int blockNum = pass1.Lines.FirstOrDefault(x => x.Address == currentLineAddress)?.BlockNumber ?? 0;
                    int offset = currentLineAddress;
                    int startB = pass1.BlockTable[blockNum].StartAddress;
                    int absoluteVal = startB + offset;
                    transformed = Regex.Replace(transformed,
                        $@"\b\*\b",
                        absoluteVal.ToString());
                }
                else
                {
                    // SI existe en TABSIM
                    if (pass1.SymbolInfoTable.ContainsKey(tk))
                    {
                        var si = pass1.SymbolInfoTable[tk];

                        // => Si IsRelative = true => offset + blockStart
                        // => Si IsRelative = false => el si.Address ya es global, NO sumamos start
                        int absoluteVal;
                        if (si.IsRelative)
                        {
                            int st = pass1.BlockTable[si.BlockNumber].StartAddress;
                            absoluteVal = st + si.Address;
                        }
                        else
                        {
                            // es ABS => si.Address ya es la dirección final
                            absoluteVal = si.Address;
                        }

                        transformed = Regex.Replace(transformed,
                            $@"\b{Regex.Escape(tk)}\b",
                            absoluteVal.ToString()
                        );
                    }
                }
            }

            // Llamar al ExpressionTypeEvaluator con diccionario vacío => da Abs
            var result = ExpressionTypeEvaluator.Evaluate(
                transformed,
                new Dictionary<string, SymbolInfo>(), // sin símbolos
                0,
                0
            );
            return result;
        }

        private string GenerateByteObject(string operand, out string error)
        {
            error = "";
            operand = operand.Trim();
            if (operand.StartsWith("C'", StringComparison.OrdinalIgnoreCase))
            {
                int i1 = operand.IndexOf('\'');
                int i2 = operand.LastIndexOf('\'');
                if (i1 < 0 || i2 <= i1)
                {
                    error = "Error: BYTE C'...' mal formado";
                    return "";
                }
                string inside = operand.Substring(i1 + 1, i2 - (i1 + 1));
                var sb = new StringBuilder();
                foreach (char c in inside)
                {
                    sb.Append(((int)c).ToString("X2"));
                }
                return sb.ToString();
            }
            else if (operand.StartsWith("X'", StringComparison.OrdinalIgnoreCase))
            {
                int i1 = operand.IndexOf('\'');
                int i2 = operand.LastIndexOf('\'');
                if (i1 < 0 || i2 <= i1)
                {
                    error = "Error: BYTE X'...' mal formado";
                    return "";
                }
                string inside = operand.Substring(i1 + 1, i2 - (i1 + 1));
                return inside.ToUpper();
            }
            else
            {
                error = "BYTE solo soporta C'...' o X'...'";
                return "";
            }
        }

        private byte ParseRegisterPair(string operand, out string error, Dictionary<string, int> regs)
        {
            error = "";
            string[] parts = operand.Split(',');
            int r1 = 0, r2 = 0;
            if (parts.Length == 1)
            {
                if (!regs.TryGetValue(parts[0].Trim(), out r1))
                {
                    error = $"Registro {parts[0]} inválido.";
                    return 0;
                }
            }
            else if (parts.Length == 2)
            {
                if (!regs.TryGetValue(parts[0].Trim(), out r1))
                {
                    error = $"Registro {parts[0]} inválido.";
                    return 0;
                }
                if (!regs.TryGetValue(parts[1].Trim(), out r2))
                {
                    error = $"Registro {parts[1]} inválido.";
                    return 0;
                }
            }
            else
            {
                error = "Error de sintaxis en F2 (demasiados registros).";
                return 0;
            }
            return (byte)((r1 << 4) | (r2 & 0xF));
        }

        private string BuildF3ObjectCode(byte opNi, bool x, int disp, bool usePC)
        {
            byte flags = 0;
            if (x) flags |= 0x80;
            if (usePC)
                flags |= 0x20;
            else
                flags |= 0x40;

            int disp12 = disp & 0xFFF;
            byte first = (byte)(flags | ((disp12 >> 8) & 0x0F));
            byte second = (byte)(disp12 & 0xFF);

            return opNi.ToString("X2") + first.ToString("X2") + second.ToString("X2");
        }

        private string BuildF4ObjectCode(byte opNi, bool x, int address20)
        {
            byte flags = 0x10; // e=1 => formato 4
            if (x) flags |= 0x80;

            int addr20 = address20 & 0xFFFFF;
            byte high4 = (byte)((addr20 >> 16) & 0x0F);
            byte mid8 = (byte)((addr20 >> 8) & 0xFF);
            byte low8 = (byte)(addr20 & 0xFF);

            flags |= high4;
            return opNi.ToString("X2") + flags.ToString("X2") + mid8.ToString("X2") + low8.ToString("X2");
        }

        private string ForceErrorObject(int maxVal, int maskVal, byte opNi, bool isF3, LineInfo ln, string errMsg)
        {
            bool x = false;
            string opn = ln.Operand?.Trim();
            if (!string.IsNullOrEmpty(opn) && opn.EndsWith(",X", StringComparison.OrdinalIgnoreCase))
            {
                x = true;
            }

            if (isF3)
            {
                byte second = (byte)(
                    ((x ? 1 : 0) << 7) |
                    (1 << 6) |
                    (1 << 5)
                );
                second |= 0x0F;
                byte third = 0xFF;
                return opNi.ToString("X2") + second.ToString("X2") + third.ToString("X2");
            }
            else
            {
                byte second = (byte)(
                    ((x ? 1 : 0) << 7) |
                    (1 << 6) |
                    (1 << 5) |
                    (1 << 4)
                );
                second |= 0x0F;
                return opNi.ToString("X2") + second.ToString("X2") + "FFFF";
            }
        }

        private void EmitTextRecord(List<string> records, int start, string objData, int lengthBytes)
        {
            string startHex = start.ToString("X6");
            string lengthHex = lengthBytes.ToString("X2");
            string textRec = $"T{startHex}{lengthHex}{objData}";
            records.Add(textRec);
        }
    }
}
