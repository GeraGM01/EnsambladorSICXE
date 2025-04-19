using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Practica02
{
    public class Pass2Visitor
    {
        // ===================================
        //             OPCODE TABLE
        // ===================================
        private Dictionary<string, byte> opcodeTable = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            // ===== Formato 1 (F1)
            { "FIX",   0xC4 },
            { "FLOAT", 0xC0 },
            { "HIO",   0xF4 },
            { "NORM",  0xC8 },
            { "SIO",   0xF0 },
            { "TIO",   0xF8 },

            // ===== Formato 2 (F2)
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

            // ===== Formato 3/4 (F3/F4)
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

        // Tabla de registros (F2)
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

        // Lista para almacenar registros de modificación para WORD
        private List<int> wordModificationAddresses = new List<int>();

        private int baseAddress = -1;  // Manejo de BASE
        public List<string> ObjRecords { get; private set; } = new List<string>();

        public void Paso2(Pass1Visitor pass1)
        {
            var lines = pass1.Lines;
            var symtab = pass1.SymbolInfoTable; // Diccionario<string, SymbolInfo>

            // Limpiar la lista de modificaciones de WORD
            wordModificationAddresses.Clear();

            for (int i = 0; i < lines.Count; i++)
            {
                var ln = lines[i];
                if (!string.IsNullOrEmpty(ln.Error))
                    continue;

                ln.ObjectCode = "";  // Por defecto sin objCode
                string op = ln.Mnemonic;
                if (string.IsNullOrEmpty(op))
                    continue;

                // Omite directivas sin objCode
                if (op.Equals("START", StringComparison.OrdinalIgnoreCase) ||
                    op.Equals("RESB", StringComparison.OrdinalIgnoreCase) ||
                    op.Equals("RESW", StringComparison.OrdinalIgnoreCase) ||
                    op.Equals("EQU", StringComparison.OrdinalIgnoreCase) ||
                    op.Equals("ORG", StringComparison.OrdinalIgnoreCase) ||
                    op.Equals("USE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Directiva END
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
                            // Poner -1 => "FFFFFF"
                            ln.ObjectCode = "FFFFFF";
                        }
                    }
                    continue;
                }

                // Directiva BASE
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
                        baseAddress = symtab[baseSymbol].Address;
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
                    string operandToEvaluate = ln.OriginalExpression;
                    if (string.IsNullOrEmpty(operandToEvaluate))
                    {
                        operandToEvaluate = ln.Operand;
                    }

                    // Evaluar expresión más compleja en WORD
                    var evalResult = ExpressionTypeEvaluator.Evaluate(operandToEvaluate, symtab, ln.Address);

                    if (evalResult.Type == ExprType.Error)
                    {
                        ln.Error = pass1.ConcatError(ln.Error, evalResult.ErrorMsg);
                    }
                    else
                    {
                        int val = evalResult.Value;
                        int mask24 = val & 0xFFFFFF;
                        ln.ObjectCode = mask24.ToString("X6");

                        // Si la expresión es relativa, agregar a la lista de modificaciones
                        // y marcar con asterisco en la tabla
                        if (evalResult.Type == ExprType.Relative)
                        {
                            // Agregar a la lista de modificaciones para generar registro M
                            wordModificationAddresses.Add(ln.Address);

                            // Marcar como relativo en la tabla (solo para visualización)
                            ln.IsRelocatable = true;
                        }
                    }
                    continue;
                }

                // =========================
                //     Instrucciones
                // =========================
                bool extended = false;
                string bareMnemonic = op;
                if (op.StartsWith("+"))
                {
                    extended = true;
                    bareMnemonic = op.Substring(1);
                }

                if (!opcodeTable.TryGetValue(bareMnemonic, out byte baseOp))
                {
                    ln.Error = pass1.ConcatError(
                        ln.Error,
                        "Instrucción no encontrada en opcodeTable"
                    );
                    continue;
                }

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
                        ln.Error = pass1.ConcatError(ln.Error, "Modo de direccionamiento no existe (F2)");
                        ln.ObjectCode = "";
                        continue;
                    }
                    ln.ObjectCode = baseOp.ToString("X2") + r1r2.ToString("X2");
                }
                // Formato 3 o 4
                else
                {
                    // RSUB
                    if (bareMnemonic.Equals("RSUB", StringComparison.OrdinalIgnoreCase))
                    {
                        byte rsubOp = (byte)((baseOp & 0xFC) | 0x03); // n=1,i=1
                        if (!extended)
                        {
                            // F3 => "4F0000"
                            ln.ObjectCode = rsubOp.ToString("X2") + "0000";
                        }
                        else
                        {
                            // F4 => "4F100000"
                            byte xbpe = 0x1; // e=1
                            ln.ObjectCode = rsubOp.ToString("X2") + xbpe.ToString("X2") + "00000";
                        }
                        continue;
                    }

                    // Normal parse de prefijos n,i
                    bool n = false, iFlag = false, x = false;
                    string operand = ln.Operand?.Trim() ?? "";

                    // Extraer el modo de direccionamiento y procesar expresiones en paréntesis si existen
                    if (operand.StartsWith("#"))
                    {
                        n = false;
                        iFlag = true;
                        operand = operand.Substring(1).Trim();

                        // Si comienza con paréntesis, es una expresión que debemos evaluar
                        if (operand.StartsWith("(") && operand.EndsWith(")"))
                        {
                            // Extraer la expresión dentro de los paréntesis
                            string expr = operand.Substring(1, operand.Length - 2);

                            // Evaluar la expresión compleja
                            var evalResult = ExpressionTypeEvaluator.Evaluate(expr, symtab, ln.Address);

                            if (evalResult.Type == ExprType.Error)
                            {
                                ln.Error = pass1.ConcatError(ln.Error, evalResult.ErrorMsg);
                                continue;
                            }

                            // Reemplazar la expresión con su valor evaluado
                            operand = evalResult.Value.ToString();
                        }
                    }
                    else if (operand.StartsWith("@"))
                    {
                        n = true;
                        iFlag = false;
                        operand = operand.Substring(1).Trim();

                        // Para modo indirecto, también procesamos expresiones
                        if (operand.StartsWith("(") && operand.EndsWith(")"))
                        {
                            string expr = operand.Substring(1, operand.Length - 2);
                            var evalResult = ExpressionTypeEvaluator.Evaluate(expr, symtab, ln.Address);

                            if (evalResult.Type == ExprType.Error)
                            {
                                ln.Error = pass1.ConcatError(ln.Error, evalResult.ErrorMsg);
                                continue;
                            }

                            operand = evalResult.Value.ToString();
                        }
                    }
                    else
                    {
                        // Modo directo por defecto
                        n = true;
                        iFlag = true;

                        // También procesamos expresiones en modo directo
                        if (operand.StartsWith("(") && operand.EndsWith(")"))
                        {
                            string expr = operand.Substring(1, operand.Length - 2);
                            var evalResult = ExpressionTypeEvaluator.Evaluate(expr, symtab, ln.Address);

                            if (evalResult.Type == ExprType.Error)
                            {
                                ln.Error = pass1.ConcatError(ln.Error, evalResult.ErrorMsg);
                                continue;
                            }

                            operand = evalResult.Value.ToString();
                        }
                    }

                    // Verificar indexado por X
                    if (operand.EndsWith(",X", StringComparison.OrdinalIgnoreCase))
                    {
                        x = true;
                        operand = operand.Substring(0, operand.Length - 2).Trim();
                    }

                    // Evaluamos el operando (símbolo o número)
                    int target = 0;
                    bool symbolNotFound = false;
                    ExprType operandType = ExprType.Absolute;

                    if (int.TryParse(operand, out target))
                    {
                        // Es un valor numérico directo, absoluto
                        operandType = ExprType.Absolute;
                    }
                    else if (symtab.ContainsKey(operand))
                    {
                        // Es un símbolo en la tabla
                        target = symtab[operand].Address;
                        operandType = symtab[operand].IsRelative ? ExprType.Relative : ExprType.Absolute;
                    }
                    else
                    {
                        // Intentamos evaluarlo como expresión
                        var evalResult = ExpressionTypeEvaluator.Evaluate(operand, symtab, ln.Address);

                        if (evalResult.Type == ExprType.Error)
                        {
                            ln.Error = pass1.ConcatError(ln.Error, "Símbolo no encontrado/EvalExpr fail");
                            symbolNotFound = true;
                        }
                        else
                        {
                            target = evalResult.Value;
                            operandType = evalResult.Type;
                        }
                    }

                    // Armar opcode con n,i
                    byte opNi = (byte)((baseOp & 0xFC) | ((n ? 1 : 0) << 1) | (iFlag ? 1 : 0));

                    bool isF3 = !extended;

                    if (isF3)
                    {
                        if (symbolNotFound)
                        {
                            ln.ObjectCode = ForceErrorObject(0xFFF, 0xFFF, opNi, true, ln, "");
                            continue;
                        }

                        int pcNext = ln.Address + 3;
                        int disp = target - pcNext;

                        // En modo inmediato, si es absoluto no necesitamos relocalizar
                        bool usePC = true;
                        if (iFlag && !n && operandType == ExprType.Absolute)
                        {
                            // Para #constante se usa el valor directamente sin PC-relative
                            disp = target;
                            usePC = false;
                        }
                        else if (usePC)
                        {
                            // Checar rango PC relative
                            bool rangeOK = (disp >= -2048 && disp <= 2047);
                            if (!rangeOK)
                            {
                                // Intentar base
                                if (baseAddress >= 0)
                                {
                                    int dispB = target - baseAddress;
                                    if (dispB >= 0 && dispB <= 4095)
                                    {
                                        disp = dispB;
                                        usePC = false; // Usamos base
                                    }
                                    else
                                    {
                                        ln.Error = pass1.ConcatError(ln.Error, "Operando fuera de rango (base)");
                                        ln.ObjectCode = ForceErrorObject(0xFFF, 0xFFF, opNi, true, ln, "");
                                        continue;
                                    }
                                }
                                else
                                {
                                    ln.Error = pass1.ConcatError(ln.Error, "No hay base y disp no cabe en PC");
                                    ln.ObjectCode = ForceErrorObject(0xFFF, 0xFFF, opNi, true, ln, "");
                                    continue;
                                }
                            }
                        }

                        ln.ObjectCode = BuildF3ObjectCode(opNi, x, baseAddress, disp, usePC);
                    }
                    else // F4
                    {
                        if (symbolNotFound)
                        {
                            ln.ObjectCode = ForceErrorObject(0xFFFFF, 0xFFFFF, opNi, false, ln, "");
                            continue;
                        }
                        if (target < 0 || target > 0xFFFFF)
                        {
                            ln.Error = pass1.ConcatError(ln.Error, "Operando fuera de rango (F4)");
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

            int startAddress = 0;
            int firstCodeAddr = -1;
            string programName = "NONAME";
            bool endHasError = false;

            // localiza START
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

            // localiza 1er code
            foreach (var ln in lines)
            {
                if (!string.IsNullOrEmpty(ln.ObjectCode))
                {
                    firstCodeAddr = ln.Address;
                    break;
                }
            }

            // localiza END
            int endSymbolAddr = -1;
            var symtab = pass1.SymbolInfoTable;
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
                        endSymbolAddr = symtab[endOp].Address;
                    }
                    break;
                }
            }

            int entryPoint;
            if (endHasError)
            {
                entryPoint = -1; // EFFFFF
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

            // Construimos T
            StringBuilder currentObj = new StringBuilder();
            int currentStart = -1;
            int bytesInRecord = 0;
            const int maxBytes = 30;

            foreach (var ln in lines)
            {
                // Al encontrar RESW/RESB, cerramos el T actual
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

                // Agregar lines con objectCode
                if (!string.IsNullOrEmpty(ln.ObjectCode))
                {
                    int lengthBytes = ln.ObjectCode.Length / 2;
                    if (currentStart < 0)
                    {
                        currentStart = ln.Address;
                        currentObj.Clear();
                        bytesInRecord = 0;
                    }
                    if (bytesInRecord + lengthBytes > maxBytes)
                    {
                        EmitTextRecord(ObjRecords, currentStart, currentObj.ToString(), bytesInRecord);
                        currentObj.Clear();
                        currentStart = ln.Address;
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

            // M => F4 (relocaciones)
            foreach (var ln in lines)
            {
                if ((ln.Format ?? "") == "F4" && !string.IsNullOrEmpty(ln.ObjectCode))
                {
                    int modAddress = ln.Address;
                    // Suele ser de 5 nibbles => "M<direcc>05+<progName>"
                    string modRec = $"M{modAddress:X6}05+{programName.ToUpper()}";
                    ObjRecords.Add(modRec);
                }
            }

            // M => WORD relocalizables (6 bytes completos)
            foreach (int wordAddress in wordModificationAddresses)
            {
                // Para WORD, modificamos los 6 nibbles (3 bytes)
                string modRec = $"M{wordAddress:X6}06+{programName.ToUpper()}";
                ObjRecords.Add(modRec);
            }

            // E
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
                    
                    // Usar la expresión original si existe para EQU y WORD
                    string opn = "";
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

                    // Agregar asterisco para indicar que es relocatable (solo en la tabla)
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

        // ================================================================
        //       MÉTODOS AUXILIARES
        // ================================================================

        private int EvaluateExpression(string expr, IReadOnlyDictionary<string, SymbolInfo> symtab, out string error)
        {
            error = "";
            expr = expr.Trim();

            // Delegar toda la evaluación al evaluador mejorado
            var result = ExpressionTypeEvaluator.Evaluate(expr, symtab, 0);

            if (result.Type == ExprType.Error)
            {
                error = result.ErrorMsg;
                return 0;
            }

            return result.Value;
        }

        private string GenerateByteObject(string operand, out string error)
        {
            error = "";
            operand = operand.Trim();

            if (operand.StartsWith("C'", StringComparison.OrdinalIgnoreCase))
            {
                int idx1 = operand.IndexOf('\'');
                int idx2 = operand.LastIndexOf('\'');
                if (idx1 < 0 || idx2 <= idx1)
                {
                    error = "Error de sintaxis en BYTE C'";
                    return "";
                }
                string inside = operand.Substring(idx1 + 1, idx2 - (idx1 + 1));
                var sb = new StringBuilder();
                foreach (char c in inside)
                {
                    sb.Append(((int)c).ToString("X2"));
                }
                return sb.ToString();
            }
            else if (operand.StartsWith("X'", StringComparison.OrdinalIgnoreCase))
            {
                int idx1 = operand.IndexOf('\'');
                int idx2 = operand.LastIndexOf('\'');
                if (idx1 < 0 || idx2 <= idx1)
                {
                    error = "Error de sintaxis en BYTE X'";
                    return "";
                }
                string inside = operand.Substring(idx1 + 1, idx2 - (idx1 + 1));
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

        private void EmitTextRecord(List<string> records, int start, string objData, int lengthBytes)
        {
            string startHex = start.ToString("X6");
            string lengthHex = lengthBytes.ToString("X2");
            string textRec = $"T{startHex}{lengthHex}{objData}";
            records.Add(textRec);
        }

        private string BuildF3ObjectCode(byte opNi, bool x, int baseAddr, int disp, bool usePC)
        {
            byte flags = 0;

            // X bit
            if (x) flags |= 0x80;

            // B bit o P bit
            if (usePC)
                flags |= 0x20; // P=1, B=0
            else
                flags |= 0x40; // B=1, P=0

            // Máscara a 12 bits
            int disp12 = disp & 0xFFF;

            // Primer byte de flags + 4 bits altos de disp
            byte first = (byte)(flags | ((disp12 >> 8) & 0x0F));

            // Segundo byte (8 bits bajos de disp)
            byte second = (byte)(disp12 & 0xFF);

            return opNi.ToString("X2") + first.ToString("X2") + second.ToString("X2");
        }

        private string BuildF4ObjectCode(byte opNi, bool x, int address20)
        {
            bool eBit = true;  // Siempre 1 en F4

            byte flags = 0x10; // e=1
            if (x) flags |= 0x80;

            int addr20 = address20 & 0xFFFFF;
            byte b2 = (byte)((addr20 >> 16) & 0x0F);
            byte b3 = (byte)((addr20 >> 8) & 0xFF);
            byte b4 = (byte)(addr20 & 0xFF);

            flags |= b2;

            return opNi.ToString("X2") + flags.ToString("X2") + b3.ToString("X2") + b4.ToString("X2");
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
                // F3 => b=1, p=1, disp=FFF
                byte second = (byte)(
                    ((x ? 1 : 0) << 7) |
                    (1 << 6) |
                    (1 << 5) |
                    (0 << 4)
                );
                second |= 0x0F;
                byte third = 0xFF;
                return opNi.ToString("X2") + second.ToString("X2") + third.ToString("X2");
            }
            else
            {
                // F4 => b=1, p=1, e=1, address=FFFFF
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
    }
}