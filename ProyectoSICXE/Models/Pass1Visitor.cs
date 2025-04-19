using System;
using System.Collections.Generic;
using System.IO;
using Antlr.Runtime;
using Antlr.Runtime.Tree;
using System.Globalization;

namespace Practica02
{
    /// <summary>
    /// Estructura para resultados de evaluación de expresiones en Pass1
    /// </summary>
    public struct ExprResult
    {
        public int Value;
        public ExprType Type;
    }

    /// <summary>
    /// Estructura principal para una línea parseada en Paso1/Paso2
    /// </summary>
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

        // Propiedad para marcar si la línea es relocatable (para WORD)
        public bool IsRelocatable { get; set; }

        // Nueva propiedad para el número de bloque
        public int BlockNumber { get; set; }

        // Nueva propiedad para mantener la expresión original
        public string OriginalExpression { get; set; }
    }

    /// <summary>
    /// Info de símbolo (dirección y si es relativo).
    /// </summary>
    public class SymbolInfo
    {
        public int Address { get; set; }
        public bool IsRelative { get; set; }

        // Nuevo campo para el número de bloque
        public int BlockNumber { get; set; }

        // Nueva propiedad para mantener la expresión original
        public string OriginalExpression { get; set; }
    }

    // Clase para almacenar información de bloques
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

        // Contador de bloques actual
        private int currentBlockNumber = 0;

        // Tabla de bloques: número => información del bloque
        private Dictionary<int, BlockInfo> blockTable = new Dictionary<int, BlockInfo>();

        // Contadores de localización por bloque
        private Dictionary<int, int> blockLocctr = new Dictionary<int, int>();

        // Tabla de símbolos
        private Dictionary<string, SymbolInfo> symbolInfoTable = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);

        // Lista con todas las líneas parseadas
        private List<LineInfo> lines = new List<LineInfo>();

        // Errores semánticos
        private List<string> semanticErrors = new List<string>();

        // Errores léx/sint simples
        public List<string> errores = new List<string>();

        // Errores léx/sint específicos
        public List<ErrorLex> LexicalErrors = new List<ErrorLex>();

        // Conjuntos: mnemonics
        private HashSet<string> form1Mnemonics = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "FIX","NORM","FLOAT","HIO","SIO","TIO"
        };
        private HashSet<string> form2Mnemonics = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "ADDR","SUBR","COMPR","MULR","DIVR","RMO","SHIFTL","SHIFTR","SVC","CLEAR","TIXR"
        };
        private HashSet<string> validMnemonics = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            // Directivas
            "START","END","BASE","BYTE","WORD","RESB","RESW","EQU","ORG","USE",
            // F1
            "FIX","NORM","FLOAT","HIO","SIO","TIO",
            // F2
            "ADDR","SUBR","COMPR","MULR","DIVR","RMO","SHIFTL","SHIFTR","SVC","CLEAR","TIXR",
            // F3/F4
            "ADD","ADDF","AND","COMP","COMPF","DIV","DIVF","J","JEQ","JGT","JLT","JSUB","LDA",
            "LDB","LDCH","LDF","LDL","LDS","LDT","LDX","MUL","MULF","MULR","OR","RD","RSUB","SSK",
            "STA","STB","STCH","STF","STI","STL","STS","STSW","STT","STX","SUB","SUBF","TIX","WD"
        };

        // Propiedades de solo lectura
        public IReadOnlyDictionary<string, SymbolInfo> SymbolInfoTable => symbolInfoTable;
        public IReadOnlyList<LineInfo> Lines => lines;
        public IReadOnlyList<string> SemanticErrors => semanticErrors;
        public int FinalLocctr => locctr;
        public IReadOnlyDictionary<int, BlockInfo> BlockTable => blockTable;

        /// <summary>
        /// Realiza el Paso1 sobre el árbol AST generado por ANTLR.
        /// </summary>
        public void Paso1(CommonTree ast)
        {
            if (ast == null) return;

            // Inicializar el bloque por defecto (0)
            blockTable[0] = new BlockInfo
            {
                Name = "",
                Number = 0,
                StartAddress = 0,
                Length = 0
            };
            blockLocctr[0] = 0;
            currentBlockNumber = 0;

            int n = ast.ChildCount;
            for (int i = 0; i < n; i++)
            {
                ITree child = ast.GetChild(i);
                ProcessLine(child);
            }

            // Calcular longitudes finales de cada bloque
            foreach (var blockNum in blockLocctr.Keys)
            {
                if (blockTable.ContainsKey(blockNum))
                {
                    blockTable[blockNum].Length = blockLocctr[blockNum];
                }
            }

            // Inyectar errores léx/sint
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

                // Buscamos la línea previa en "lines" con SourceLine < errLine
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

        /// <summary>
        /// Procesa un nodo (línea) del AST: determina la etiqueta, opcode, operando, etc.
        /// </summary>
        private void ProcessLine(ITree lineNode)
        {
            if (lineNode == null) return;

            if (lineNode.Text == "INSTR" || lineNode.Text == "DIR")
            {
                int lineSource = lineNode.Line;
                string label = "", opcode = "", operand = "", format = "-";
                string error = "";
                int inc = 0;

                int ccount = lineNode.ChildCount;
                if (ccount == 0) return;

                int index = 0;
                var firstChild = lineNode.GetChild(0);
                if (firstChild.Type == Gram_SICXEParser.ID)
                {
                    label = firstChild.Text;
                    index++;
                }

                // Detectar si viene un '+' (formato 4)
                if (index < ccount)
                {
                    var tok = lineNode.GetChild(index).Text;
                    if (tok == "+" && (index + 1 < ccount))
                    {
                        opcode = "+" + lineNode.GetChild(index + 1).Text;
                        index += 2;
                    }
                    else
                    {
                        opcode = tok;
                        index++;
                    }
                }

                // Operando
                if (index < ccount)
                {
                    var parts = new List<string>();
                    while (index < ccount)
                    {
                        var tk = lineNode.GetChild(index).Text;
                        if ((tk == "#" || tk == "@") && (index + 1 < ccount))
                        {
                            // Ej: '#' + 'NUMERO', '@' + 'COUNT'
                            parts.Add(tk + lineNode.GetChild(index + 1).Text);
                            index += 2;
                        }
                        else
                        {
                            parts.Add(tk);
                            index++;
                        }
                    }
                    operand = string.Join("", parts);
                }

                // Manejo especial de START
                if (opcode.Equals("START", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(operand))
                    {
                        error = ConcatError(error, "Error: Falta operando en START");
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

                // Manejo especial de END
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

                // Manejo especial de USE (para bloques)
                if (opcode.Equals("USE", StringComparison.OrdinalIgnoreCase))
                {
                    // Guardar el contador de localización actual para el bloque actual
                    blockLocctr[currentBlockNumber] = locctr;

                    // Cambiar al nuevo bloque
                    if (string.IsNullOrEmpty(operand))
                    {
                        // USE sin operando => volver al bloque por defecto (0)
                        currentBlockNumber = 0;
                    }
                    else
                    {
                        // Buscar si el bloque ya existe
                        int targetBlock = -1;
                        foreach (var kvp in blockTable)
                        {
                            if (kvp.Value.Name.Equals(operand, StringComparison.OrdinalIgnoreCase))
                            {
                                targetBlock = kvp.Key;
                                break;
                            }
                        }

                        if (targetBlock < 0)
                        {
                            // Crear nuevo bloque
                            int newBlockNum = blockTable.Count;
                            blockTable[newBlockNum] = new BlockInfo
                            {
                                Name = operand,
                                Number = newBlockNum,
                                StartAddress = 0, // Se calculará después
                                Length = 0
                            };

                            if (!blockLocctr.ContainsKey(newBlockNum))
                            {
                                blockLocctr[newBlockNum] = 0;
                            }

                            currentBlockNumber = newBlockNum;
                        }
                        else
                        {
                            // Usar bloque existente
                            currentBlockNumber = targetBlock;
                        }
                    }

                    // Actualizar el contador de localización al del bloque actual
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

                // Valida que el opcode exista (o directiva)
                string checkOpcode = opcode.StartsWith("+") ? opcode.Substring(1) : opcode;
                if (!validMnemonics.Contains(checkOpcode))
                {
                    error = ConcatError(error, "Error: Instrucción o Directiva no existe");
                }

                // Determina formato
                if (IsDirective(checkOpcode))
                {
                    format = "-";
                    inc = 0;
                }
                else if (opcode.StartsWith("+"))
                {
                    format = "F4";
                    inc = 4;
                }
                else if (form1Mnemonics.Contains(checkOpcode))
                {
                    format = "F1";
                    inc = 1;
                }
                else if (form2Mnemonics.Contains(checkOpcode))
                {
                    format = "F2";
                    inc = 2;
                }
                else
                {
                    format = "F3";
                    inc = 3;
                }

                // Directivas específicas
                if (opcode.Equals("RESW", StringComparison.OrdinalIgnoreCase))
                {
                    var res = ExpressionTypeEvaluator.Evaluate(operand, symbolInfoTable, locctr);
                    if (res.Type == ExprType.Error)
                    {
                        error = ConcatError(error, res.ErrorMsg);
                    }
                    else
                    {
                        inc = res.Value * 3;
                    }
                }
                else if (opcode.Equals("RESB", StringComparison.OrdinalIgnoreCase))
                {
                    var res = ExpressionTypeEvaluator.Evaluate(operand, symbolInfoTable, locctr);
                    if (res.Type == ExprType.Error)
                    {
                        error = ConcatError(error, res.ErrorMsg);
                    }
                    else
                    {
                        inc = res.Value;
                    }
                }
                else if (opcode.Equals("WORD", StringComparison.OrdinalIgnoreCase))
                {
                    inc = 3;
                }
                else if (opcode.Equals("BYTE", StringComparison.OrdinalIgnoreCase))
                {
                    int r = ComputeBYTE(operand, out string e2);
                    if (!string.IsNullOrEmpty(e2)) error = ConcatError(error, e2);
                    inc = r;
                }
                else if (opcode.Equals("BASE", StringComparison.OrdinalIgnoreCase))
                {
                    // BASE no incrementa locctr
                    inc = 0;
                }
                else if (opcode.Equals("EQU", StringComparison.OrdinalIgnoreCase))
                {
                    // EQU => no incrementa locctr; define un símbolo
                    if (string.IsNullOrEmpty(label))
                    {
                        error = ConcatError(error, "Error: EQU sin etiqueta");
                    }
                    else
                    {
                        var res = ExpressionTypeEvaluator.Evaluate(operand, symbolInfoTable, locctr);
                        if (res.Type == ExprType.Error)
                        {
                            error = ConcatError(error, res.ErrorMsg);
                            // Se podría insertar con valor 0, ABS
                            symbolInfoTable[label] = new SymbolInfo
                            {
                                Address = 0,
                                IsRelative = false,
                                BlockNumber = currentBlockNumber,
                                OriginalExpression = operand
                            };
                        }
                        else
                        {
                            bool isRel = (res.Type == ExprType.Relative);
                            symbolInfoTable[label] = new SymbolInfo
                            {
                                Address = res.Value,
                                IsRelative = isRel,
                                BlockNumber = currentBlockNumber,
                                OriginalExpression = operand
                            };
                        }
                    }
                    inc = 0;
                }
                else if (opcode.Equals("ORG", StringComparison.OrdinalIgnoreCase))
                {
                    // ORG => cambia locctr al valor
                    var res = ExpressionTypeEvaluator.Evaluate(operand, symbolInfoTable, locctr);
                    if (res.Type == ExprType.Error)
                    {
                        error = ConcatError(error, res.ErrorMsg);
                    }
                    else
                    {
                        if (res.Type == ExprType.Relative)
                        {
                            error = ConcatError(error, "ORG con dirección relativa no es soportada");
                        }
                        else
                        {
                            locctr = res.Value;
                            blockLocctr[currentBlockNumber] = locctr;
                        }
                    }
                    inc = 0;
                }

                // Insertar etiqueta (si no es START, EQU, etc.)
                if (!string.IsNullOrEmpty(label) &&
                    !opcode.Equals("START", StringComparison.OrdinalIgnoreCase) &&
                    !opcode.Equals("EQU", StringComparison.OrdinalIgnoreCase))
                {
                    // Checar si ya existe
                    if (symbolInfoTable.ContainsKey(label))
                    {
                        string msg = $"(Línea {lineSource}) Error: símbolo duplicado.";
                        semanticErrors.Add(msg);
                        error = ConcatError(error, "Error: símbolo duplicado");
                    }
                    else
                    {
                        // Por defecto, las etiquetas de código/datos son relativas
                        symbolInfoTable[label] = new SymbolInfo
                        {
                            Address = locctr,
                            IsRelative = true,
                            BlockNumber = currentBlockNumber,
                            OriginalExpression = ""
                        };
                    }
                }

                // Agregar la línea con la expresión original para EQU y WORD
                string originalExpr = "";
                if (opcode.Equals("EQU", StringComparison.OrdinalIgnoreCase) ||
                    opcode.Equals("WORD", StringComparison.OrdinalIgnoreCase))
                {
                    originalExpr = operand;
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
                    OriginalExpression = originalExpr
                });

                // Incrementar locctr si corresponde
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
                return inside.Length; // Cada carácter => 1 byte
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
                // Cadena de hex => cada 2 dígitos = 1 byte
                int nibbleCount = inside.Length;
                return (nibbleCount + 1) / 2;
            }
            else
            {
                error = "Error: BYTE solo soporta C'...' o X'...'";
                return 0;
            }
        }

        /// <summary>
        /// Parsea un string como decimal o hex (sufijo 'H').
        /// </summary>
        private int ParseHexOrDecimal(string s)
        {
            s = s.Trim();
            if (string.IsNullOrEmpty(s)) return 0;

            // Hex con sufijo H
            if (s.EndsWith("H", StringComparison.OrdinalIgnoreCase))
            {
                string hexPart = s.Substring(0, s.Length - 1);
                if (int.TryParse(hexPart, NumberStyles.HexNumber, null, out int valHex))
                    return valHex;
                return 0;
            }

            // Decimal
            if (int.TryParse(s, out int valDec))
                return valDec;

            // No parseado => 0
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
                sw.WriteLine("=== TABLA PRINCIPAL (CP, Símbolo, Instrucción, Operando, Formato, Error) ===");
                sw.WriteLine("CP       Símbolo     Instrucción   Operando   Formato   Error");
                foreach (var ln in lines)
                {
                    string cpHex = ln.Address.ToString("X6");
                    string sym = ln.Label ?? "-";
                    string mnemo = ln.Mnemonic ?? "-";

                    // Usar la expresión original si existe
                    string oper = !string.IsNullOrEmpty(ln.OriginalExpression) ? ln.OriginalExpression : (ln.Operand ?? "-");

                    string fmt = ln.Format ?? "-";
                    string err = ln.Error ?? "";
                    sw.WriteLine($"{cpHex,-8}  {sym,-10}  {mnemo,-12}  {oper,-10}  {fmt,-7}  {err}");
                }
                sw.WriteLine();

                if (symbolInfoTable.Count > 0)
                {
                    sw.WriteLine("=== TABSIC (Símbolo, Dirección, Tipo) ===");
                    foreach (var kvp in symbolInfoTable)
                    {
                        string name = kvp.Key;
                        int addr = kvp.Value.Address;
                        bool isRel = kvp.Value.IsRelative;
                        int blockNum = kvp.Value.BlockNumber;

                        sw.WriteLine($"{name,-12}  0x{addr:X6}  {(isRel ? "REL" : "ABS")}  {blockNum}");
                    }
                    sw.WriteLine();
                }

                // Tabla de bloques
                sw.WriteLine("\nTabla de Bloques (No., Nombre, Dirección, Longitud):");
                sw.WriteLine("----------------------------------------------");
                foreach (var kvp in blockTable)
                {
                    int blockNum = kvp.Key;
                    string blockName = kvp.Value.Name;
                    if (string.IsNullOrEmpty(blockName))
                        blockName = "(por omisión)";
                    int startAddr = kvp.Value.StartAddress;
                    int length = kvp.Value.Length;

                    sw.WriteLine($"{blockNum,-5}  {blockName,-15}  0x{startAddr:X6}  0x{length:X6}");
                }
            }
        }

        public void DumpAllErrorsToFile(string filePath)
        {
            using (var sw = new StreamWriter(filePath, false))
            {
                // 1) Errores léx/sint
                if (errores.Count > 0)
                {
                    sw.WriteLine("=== ERRORES LEXICOS/SINTACTICOS ===");
                    foreach (var e in errores) sw.WriteLine(e);
                    sw.WriteLine();
                }

                // 2) Errores en lines
                bool anyErr = false;
                foreach (var ln in lines)
                {
                    if (!string.IsNullOrEmpty(ln.Error))
                    {
                        anyErr = true;
                        sw.WriteLine($"[Línea {ln.SourceLine}] (CP=0x{ln.Address:X4}) " +
                                     $"LABEL='{ln.Label}' MNEMONIC='{ln.Mnemonic}' " +
                                     $"OPERAND='{ln.Operand}' => {ln.Error}");
                    }
                }
                if (anyErr) sw.WriteLine();

                // 3) Errores semánticos
                if (semanticErrors.Count > 0)
                {
                    sw.WriteLine("=== ERRORES SEMANTICOS ===");
                    foreach (var sE in semanticErrors) sw.WriteLine(sE);
                    sw.WriteLine();
                }
            }
        }
    }
}