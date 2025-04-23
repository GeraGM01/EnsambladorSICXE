using System;
using System.Collections.Generic;
using System.IO;
using Antlr.Runtime;
using Antlr.Runtime.Tree;
using System.Globalization;
using System.Linq;  // si quisieras asignar StartAddress luego

namespace Practica02
{
    public struct ExprResult
    {
        public int Value;
        public ExprType Type;
    }

    public class LineInfo
    {
        public int SourceLine { get; set; }
        public int Address { get; set; }
        public string Label { get; set; }
        public string Mnemonic { get; set; }
        public string Operand { get; set; }
        public string Format { get; set; }
        public string Error { get; set; }
        public string ObjectCode { get; set; }
        public bool IsRelocatable { get; set; }
        public int BlockNumber { get; set; }
        public string OriginalExpression { get; set; }
    }

    public class SymbolInfo
    {
        public int Address { get; set; }
        public bool IsRelative { get; set; }
        public int BlockNumber { get; set; }
        public string OriginalExpression { get; set; }
    }

    public class BlockInfo
    {
        public string Name { get; set; }
        public int Number { get; set; }
        public int StartAddress { get; set; }
        public int Length { get; set; }
    }

    public class Pass1Visitor
    {
        private int locctr = 0;
        private int currentBlockNumber = 0;

        private Dictionary<int, BlockInfo> blockTable = new Dictionary<int, BlockInfo>();
        private Dictionary<int, int> blockLocctr = new Dictionary<int, int>();

        private Dictionary<string, SymbolInfo> symbolInfoTable
            = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);

        private List<LineInfo> lines = new List<LineInfo>();
        private List<string> semanticErrors = new List<string>();

        public List<string> errores = new List<string>();
        public List<ErrorLex> LexicalErrors = new List<ErrorLex>();

        private HashSet<string> form1Mnemonics = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "FIX","NORM","FLOAT","HIO","SIO","TIO"
        };
        private HashSet<string> form2Mnemonics = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "ADDR","SUBR","COMPR","MULR","DIVR","RMO","SHIFTL","SHIFTR","SVC","CLEAR","TIXR"
        };
        private HashSet<string> validMnemonics = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "START","END","BASE","BYTE","WORD","RESB","RESW","EQU","ORG","USE",
            "FIX","NORM","FLOAT","HIO","SIO","TIO",
            "ADDR","SUBR","COMPR","MULR","DIVR","RMO","SHIFTL","SHIFTR","SVC","CLEAR","TIXR",
            "ADD","ADDF","AND","COMP","COMPF","DIV","DIVF","J","JEQ","JGT","JLT","JSUB","LDA",
            "LDB","LDCH","LDF","LDL","LDS","LDT","LDX","MUL","MULF","MULR","OR","RD","RSUB","SSK",
            "STA","STB","STCH","STF","STI","STL","STS","STSW","STT","STX","SUB","SUBF","TIX","WD"
        };

        public IReadOnlyDictionary<string, SymbolInfo> SymbolInfoTable => symbolInfoTable;
        public IReadOnlyList<LineInfo> Lines => lines;
        public IReadOnlyList<string> SemanticErrors => semanticErrors;
        public int FinalLocctr => locctr;
        public IReadOnlyDictionary<int, BlockInfo> BlockTable => blockTable;

        public void Paso1(CommonTree ast)
        {
            if (ast == null) return;

            // Bloque 0 por omisión
            blockTable[0] = new BlockInfo
            {
                Name = "",
                Number = 0,
                StartAddress = 0,
                Length = 0
            };
            blockLocctr[0] = 0;
            currentBlockNumber = 0;

            // Recorremos AST
            int n = ast.ChildCount;
            for (int i = 0; i < n; i++)
            {
                ITree child = ast.GetChild(i);
                ProcessLine(child);
            }

            // Longitud final
            foreach (var bNum in blockLocctr.Keys)
            {
                if (blockTable.ContainsKey(bNum))
                {
                    var bi = blockTable[bNum];
                    bi.Length = blockLocctr[bNum];
                    blockTable[bNum] = bi;
                }
            }

            // Asignar StartAddress a cada bloque
            {
                int start = 0;
                var sortedB = blockTable.Keys.OrderBy(k => k).ToList();
                foreach (int b in sortedB)
                {
                    var infoB = blockTable[b];
                    infoB.StartAddress = start;
                    blockTable[b] = infoB;
                    start += infoB.Length;
                }
            }

            InjectLexicalErrors();
        }

        private void InjectLexicalErrors()
        {
            LexicalErrors.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));
            foreach (var lexErr in LexicalErrors)
            {
                int errLine = lexErr.LineNumber;
                string rawLine = lexErr.RawLine;
                string eMsg = lexErr.ErrorMsg;

                string labelX = "";
                string mnemonicX = "???";
                string operandX = "";

                var splitted = rawLine.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (splitted.Length >= 3)
                {
                    labelX = splitted[0];
                    mnemonicX = splitted[1];
                    operandX = string.Join(" ", splitted, 2, splitted.Length - 2);
                }
                else if (splitted.Length == 2)
                {
                    mnemonicX = splitted[0];
                    operandX = splitted[1];
                }
                else if (splitted.Length == 1)
                {
                    mnemonicX = splitted[0];
                }

                int prevIndex = -1;
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    if (lines[i].SourceLine < errLine)
                    {
                        prevIndex = i;
                        break;
                    }
                }

                int chosenCP = 0;
                if (prevIndex >= 0) chosenCP = lines[prevIndex].Address;

                var newLine = new LineInfo
                {
                    SourceLine = errLine,
                    Address = chosenCP,
                    Label = labelX,
                    Mnemonic = mnemonicX,
                    Operand = operandX,
                    Format = "-",
                    Error = eMsg,
                    BlockNumber = currentBlockNumber
                };

                int insertPos = prevIndex + 1;
                if (insertPos < 0) insertPos = 0;
                lines.Insert(insertPos, newLine);
            }
        }

        private void ProcessLine(ITree lineNode)
        {
            if (lineNode == null) return;

            if (lineNode.Text == "INSTR" || lineNode.Text == "DIR")
            {
                int lineSource = lineNode.Line;
                string label = "";
                string opcode = "";
                string operand = "";
                string format = "-";
                string error = "";
                int inc = 0;

                int countChildren = lineNode.ChildCount;
                if (countChildren == 0) return;

                int idx = 0;
                var firstCh = lineNode.GetChild(0);
                if (firstCh.Type == Gram_SICXEParser.ID)
                {
                    label = firstCh.Text;
                    idx++;
                }

                // Detecta '+' => formato 4
                if (idx < countChildren)
                {
                    var tk = lineNode.GetChild(idx).Text;
                    if (tk == "+" && (idx + 1 < countChildren))
                    {
                        opcode = "+" + lineNode.GetChild(idx + 1).Text;
                        idx += 2;
                    }
                    else
                    {
                        opcode = tk;
                        idx++;
                    }
                }

                // Toma el resto como operando
                if (idx < countChildren)
                {
                    var opParts = new List<string>();
                    while (idx < countChildren)
                    {
                        var tkk = lineNode.GetChild(idx).Text;
                        if ((tkk == "#" || tkk == "@") && (idx + 1 < countChildren))
                        {
                            opParts.Add(tkk + lineNode.GetChild(idx + 1).Text);
                            idx += 2;
                        }
                        else
                        {
                            opParts.Add(tkk);
                            idx++;
                        }
                    }
                    operand = string.Join("", opParts);
                }

                // START
                if (opcode.Equals("START", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(operand))
                    {
                        error = ConcatError(error, "Falta operando en START");
                    }
                    else
                    {
                        locctr = ParseHexOrDecimal(operand);
                        blockLocctr[currentBlockNumber] = locctr;
                    }
                    lines.Add(new LineInfo
                    {
                        SourceLine = lineSource,
                        Address = locctr,
                        Label = label,
                        Mnemonic = opcode,
                        Operand = operand,
                        Format = "-",
                        Error = error,
                        BlockNumber = currentBlockNumber
                    });
                    return;
                }

                // END
                if (opcode.Equals("END", StringComparison.OrdinalIgnoreCase))
                {
                    lines.Add(new LineInfo
                    {
                        SourceLine = lineSource,
                        Address = locctr,
                        Label = label,
                        Mnemonic = opcode,
                        Operand = operand,
                        Format = "-",
                        Error = error,
                        BlockNumber = currentBlockNumber
                    });
                    return;
                }

                // USE => cambio de bloque
                if (opcode.Equals("USE", StringComparison.OrdinalIgnoreCase))
                {
                    blockLocctr[currentBlockNumber] = locctr;

                    if (string.IsNullOrEmpty(operand))
                    {
                        currentBlockNumber = 0;
                    }
                    else
                    {
                        int foundBlock = -1;
                        foreach (var kv in blockTable)
                        {
                            if (kv.Value.Name.Equals(operand, StringComparison.OrdinalIgnoreCase))
                            {
                                foundBlock = kv.Key;
                                break;
                            }
                        }
                        if (foundBlock < 0)
                        {
                            int newB = blockTable.Count;
                            blockTable[newB] = new BlockInfo
                            {
                                Name = operand,
                                Number = newB,
                                StartAddress = 0,
                                Length = 0
                            };
                            if (!blockLocctr.ContainsKey(newB)) blockLocctr[newB] = 0;
                            currentBlockNumber = newB;
                        }
                        else
                        {
                            currentBlockNumber = foundBlock;
                        }
                    }
                    locctr = blockLocctr[currentBlockNumber];

                    lines.Add(new LineInfo
                    {
                        SourceLine = lineSource,
                        Address = locctr,
                        Label = label,
                        Mnemonic = opcode,
                        Operand = operand,
                        Format = "-",
                        Error = error,
                        BlockNumber = currentBlockNumber
                    });
                    return;
                }

                // Instrucción/Directiva
                string checkOp = opcode.StartsWith("+") ? opcode.Substring(1) : opcode;
                if (!validMnemonics.Contains(checkOp))
                {
                    error = ConcatError(error, "Instrucción/Directiva no existe");
                }

                // Formato
                if (IsDirective(checkOp))
                {
                    format = "-";
                    inc = 0;
                }
                else if (opcode.StartsWith("+"))
                {
                    format = "F4";
                    inc = 4;
                }
                else if (form1Mnemonics.Contains(checkOp))
                {
                    format = "F1";
                    inc = 1;
                }
                else if (form2Mnemonics.Contains(checkOp))
                {
                    format = "F2";
                    inc = 2;
                }
                else
                {
                    format = "F3";
                    inc = 3;
                }

                // Directivas
                if (opcode.Equals("RESW", StringComparison.OrdinalIgnoreCase))
                {
                    var evr = ExpressionTypeEvaluator.Evaluate(
                        operand, symbolInfoTable, locctr, currentBlockNumber, false
                    );
                    if (evr.Type == ExprType.Error)
                    {
                        error = ConcatError(error, evr.ErrorMsg);
                    }
                    else
                    {
                        inc = evr.Value * 3;
                    }
                }
                else if (opcode.Equals("RESB", StringComparison.OrdinalIgnoreCase))
                {
                    var evr = ExpressionTypeEvaluator.Evaluate(
                        operand, symbolInfoTable, locctr, currentBlockNumber, false
                    );
                    if (evr.Type == ExprType.Error)
                    {
                        error = ConcatError(error, evr.ErrorMsg);
                    }
                    else
                    {
                        inc = evr.Value;
                    }
                }
                else if (opcode.Equals("WORD", StringComparison.OrdinalIgnoreCase))
                {
                    inc = 3;
                }
                else if (opcode.Equals("BYTE", StringComparison.OrdinalIgnoreCase))
                {
                    inc = ComputeBYTE(operand, out string eByte);
                    if (!string.IsNullOrEmpty(eByte)) error = ConcatError(error, eByte);
                }
                else if (opcode.Equals("BASE", StringComparison.OrdinalIgnoreCase))
                {
                    inc = 0;
                }
                else if (opcode.Equals("EQU", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(label))
                    {
                        error = ConcatError(error, "EQU sin etiqueta");
                    }
                    else
                    {
                        var eqRes = ExpressionTypeEvaluator.Evaluate(
                            operand, symbolInfoTable, locctr, currentBlockNumber, true
                        );
                        if (eqRes.Type == ExprType.Error)
                        {
                            error = ConcatError(error, eqRes.ErrorMsg);
                            symbolInfoTable[label] = new SymbolInfo
                            {
                                Address = 0xFFFF,
                                IsRelative = false,
                                BlockNumber = currentBlockNumber,
                                OriginalExpression = operand
                            };
                        }
                        else
                        {
                            symbolInfoTable[label] = new SymbolInfo
                            {
                                Address = eqRes.Value,
                                IsRelative = (eqRes.Type == ExprType.Relative),
                                BlockNumber = currentBlockNumber,
                                OriginalExpression = operand
                            };
                        }
                    }
                    inc = 0;
                }
                else if (opcode.Equals("ORG", StringComparison.OrdinalIgnoreCase))
                {
                    var orgRes = ExpressionTypeEvaluator.Evaluate(
                        operand, symbolInfoTable, locctr, currentBlockNumber, false
                    );
                    if (orgRes.Type == ExprType.Error)
                    {
                        error = ConcatError(error, orgRes.ErrorMsg);
                    }
                    else
                    {
                        if (orgRes.Type == ExprType.Relative)
                        {
                            error = ConcatError(error, "ORG con dirección relativa no soportada");
                        }
                        else
                        {
                            locctr = orgRes.Value;
                            blockLocctr[currentBlockNumber] = locctr;
                        }
                    }
                    inc = 0;
                }

                // Definir símbolo
                if (!string.IsNullOrEmpty(label)
                    && !opcode.Equals("START", StringComparison.OrdinalIgnoreCase)
                    && !opcode.Equals("EQU", StringComparison.OrdinalIgnoreCase))
                {
                    if (symbolInfoTable.ContainsKey(label))
                    {
                        string msg = $"(Línea {lineSource}) Símbolo duplicado";
                        semanticErrors.Add(msg);
                        error = ConcatError(error, "Símbolo duplicado");
                    }
                    else
                    {
                        symbolInfoTable[label] = new SymbolInfo
                        {
                            Address = locctr,
                            IsRelative = true,
                            BlockNumber = currentBlockNumber,
                            OriginalExpression = ""
                        };
                    }
                }

                // Guardar la expresión original si es EQU/WORD
                string origExpr = "";
                if (opcode.Equals("EQU", StringComparison.OrdinalIgnoreCase) ||
                    opcode.Equals("WORD", StringComparison.OrdinalIgnoreCase))
                {
                    origExpr = operand;
                }

                lines.Add(new LineInfo
                {
                    SourceLine = lineSource,
                    Address = locctr,
                    Label = label,
                    Mnemonic = opcode,
                    Operand = operand,
                    Format = format,
                    Error = error,
                    BlockNumber = currentBlockNumber,
                    OriginalExpression = origExpr
                });

                locctr += inc;
                blockLocctr[currentBlockNumber] = locctr;
            }
        }

        private bool IsDirective(string op)
        {
            return op.Equals("BASE", StringComparison.OrdinalIgnoreCase) ||
                   op.Equals("BYTE", StringComparison.OrdinalIgnoreCase) ||
                   op.Equals("WORD", StringComparison.OrdinalIgnoreCase) ||
                   op.Equals("RESB", StringComparison.OrdinalIgnoreCase) ||
                   op.Equals("RESW", StringComparison.OrdinalIgnoreCase) ||
                   op.Equals("START", StringComparison.OrdinalIgnoreCase) ||
                   op.Equals("END", StringComparison.OrdinalIgnoreCase) ||
                   op.Equals("EQU", StringComparison.OrdinalIgnoreCase) ||
                   op.Equals("ORG", StringComparison.OrdinalIgnoreCase) ||
                   op.Equals("USE", StringComparison.OrdinalIgnoreCase);
        }

        private int ComputeBYTE(string operand, out string error)
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
                    return 0;
                }
                string inside = operand.Substring(i1 + 1, i2 - (i1 + 1));
                return inside.Length;
            }
            else if (operand.StartsWith("X'", StringComparison.OrdinalIgnoreCase))
            {
                int i1 = operand.IndexOf('\'');
                int i2 = operand.LastIndexOf('\'');
                if (i1 < 0 || i2 <= i1)
                {
                    error = "Error: BYTE X'...' mal formado";
                    return 0;
                }
                string inside = operand.Substring(i1 + 1, i2 - (i1 + 1));
                return (inside.Length + 1) / 2;
            }
            else
            {
                error = "Error: BYTE soporta C'...' o X'...'";
                return 0;
            }
        }

        private int ParseHexOrDecimal(string s)
        {
            s = s.Trim();
            if (string.IsNullOrEmpty(s)) return 0;

            if (s.EndsWith("H", StringComparison.OrdinalIgnoreCase))
            {
                string hexPart = s.Substring(0, s.Length - 1);
                if (int.TryParse(hexPart, NumberStyles.HexNumber, null, out int valHex))
                    return valHex;
                return 0;
            }
            if (int.TryParse(s, out int valDec))
                return valDec;

            return 0;
        }

        public string ConcatError(string existing, string newErr)
        {
            if (string.IsNullOrEmpty(existing)) return newErr;
            return existing + " / " + newErr;
        }

        public void DumpTablesToFile(string filePath)
        {
            using (var sw = new StreamWriter(filePath, false))
            {
                sw.WriteLine("=== TABLA PRINCIPAL (CP, Símbolo, Instr, Operando, Formato, Error) ===");
                sw.WriteLine("CP    Símbolo   Instr   Operando   Formato  Error");
                foreach (var ln in lines)
                {
                    string cpHex = ln.Address.ToString("X6");
                    string sym = ln.Label ?? "-";
                    string mnemo = ln.Mnemonic ?? "-";
                    string oper = string.IsNullOrEmpty(ln.OriginalExpression)
                                  ? (ln.Operand ?? "-")
                                  : ln.OriginalExpression;
                    string fmt = ln.Format ?? "-";
                    string err = ln.Error ?? "";
                    sw.WriteLine($"{cpHex,-6} {sym,-8} {mnemo,-8} {oper,-10} {fmt,-5} {err}");
                }

                sw.WriteLine();
                if (symbolInfoTable.Count > 0)
                {
                    sw.WriteLine("=== TABSIC (Símbolo, Dirección, Tipo, Bloque) ===");
                    foreach (var kvp in symbolInfoTable)
                    {
                        string name = kvp.Key;
                        var si = kvp.Value;
                        string relOrAbs = si.IsRelative ? "REL" : "ABS";
                        sw.WriteLine($"{name,-10} 0x{si.Address:X6} {relOrAbs} {si.BlockNumber}");
                    }
                    sw.WriteLine();
                }
                sw.WriteLine("=== Tabla de Bloques ===");
                foreach (var bkv in blockTable)
                {
                    int nb = bkv.Key;
                    var bi = bkv.Value;
                    sw.WriteLine($"Bloque {nb}: nombre='{bi.Name}' start={bi.StartAddress} length=0x{bi.Length:X4}");
                }
            }
        }

        public void DumpAllErrorsToFile(string filePath)
        {
            using (var sw = new StreamWriter(filePath, false))
            {
                if (errores.Count > 0)
                {
                    sw.WriteLine("=== Errores Léxicos/Sintácticos ===");
                    foreach (var e in errores) sw.WriteLine(e);
                    sw.WriteLine();
                }

                bool anyError = false;
                foreach (var ln in lines)
                {
                    if (!string.IsNullOrEmpty(ln.Error))
                    {
                        anyError = true;
                        sw.WriteLine($"[Línea {ln.SourceLine}] CP=0x{ln.Address:X4} '{ln.Label}' '{ln.Mnemonic}' => {ln.Error}");
                    }
                }
                if (anyError) sw.WriteLine();

                if (semanticErrors.Count > 0)
                {
                    sw.WriteLine("=== Errores Semánticos ===");
                    foreach (var sErr in semanticErrors) sw.WriteLine(sErr);
                    sw.WriteLine();
                }
            }
        }
    }

}
