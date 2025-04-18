grammar Gram_SICXE;

options {
    language = CSharp3;
    output   = AST;
}

tokens {
    INSTR;
    DIR;
}

// =========================
// REGLAS DEL PARSER
// =========================

public programa
    : inicio proposiciones fin
    ;

// "EJER1 START 0"
inicio
    : raiz
    | proposicion
    ;

raiz
    : et=etiqueta s=START v=(NUM | HEXSUF)? FINL
      -> ^(DIR $et $s $v?)
    ;

fin
    : e=END ent=entrada FINL
      -> ^(INSTR $e $ent?)
    ;


entrada
    : ID?
    ;

// Varias proposiciones
proposiciones
    : (proposicion)+
    ;

// Cada proposicion es instruccion o directiva
proposicion
    : instruccion
    | directiva
    ;

// instruccion => ^(INSTR (etiqueta)? opinstruccion)
instruccion
    : etiqueta? opinstruccion FINL
      -> ^(INSTR (etiqueta)? opinstruccion)
    ;

// directiva => ^(DIR (etiqueta)? tipodirectiva (opdirectiva)?)
directiva
    : etiqueta? tipodirectiva (opdirectiva)? FINL
      -> ^(DIR (etiqueta)? tipodirectiva (opdirectiva)?)
    ;

// Directivas (BYTE,WORD,RESB,RESW,BASE)
tipodirectiva
    : BYTE
    | WORD
    | RESB
    | RESW
    | BASE
    ;

// Etiqueta => ID
etiqueta
    : ID
    ;

// opinstruccion => formato
opinstruccion
    : formato
    ;

// formato => f1|f2|f3|f4|indexado
formato
    : f1
    | f2
    | f3
    | f4
    | indexado
    ;

// f1 => F1CODOP (sin operandos)
f1
    : F1CODOP
    ;

// f2 => F2CODOP + operandos (REG, etc.)
f2
    : F2CODOP REG
    | F2CODOP REG ',' REG
    | F2CODOP REG ',' NUM
    ;

// f3 => simple3 | indirecto3 | inmediato3
f3
    : simple3
    | indirecto3
    | inmediato3
    ;

// f4 => '+' f3
f4
    : PLUS f3
    ;

// indexado => F34CODOP INDICE
indexado
    : F34CODOP INDICE
    ;

// simple3 => F34CODOP (ID|NUM)? (',' REG)?
simple3
    : F34CODOP (ID | NUM)? (',' REG )?
    ;

// indirecto3 => F34CODOP '@' (NUM|ID)
indirecto3
    : F34CODOP AT (NUM|ID)
    ;

// inmediato3 => F34CODOP '#' (NUM|ID)
inmediato3
    : F34CODOP NUMERAL (NUM|ID)
    ;

// opdirectiva => operandos de directiva
opdirectiva
    : NUM
    | CONSTHEX
    | CONSTCAD
    | HEXSUF
    | ID
    | CHCONST
    | HEXCONST
    ;

// =========================
// LEXER
// =========================

START : 'START';
END   : 'END';
BYTE  : 'BYTE';
WORD  : 'WORD';
RESB  : 'RESB';
RESW  : 'RESW';
BASE  : 'BASE';

// FINL
FINL
    : ( ';' )* ( '\r'? '\n' | '\r' | EOF )
    ;

// Registros
REG
    : 'A' | 'B' | 'C' | 'D' | 'E' | 'F' | 'X'
    ;

// Indice
INDICE
    : 'IX' | 'IY' | 'IZ'
    ;

// '+' => PLUS
PLUS
    : '+'
    ;

// Declaramos tokens '@','#'
AT
    : '@'
    ;

NUMERAL
    : '#'
    ;

// Mnemo?nicos f1,f2,f3/4
fragment F1_INSTR
    : 'FIX'
    | 'NORM'
    | 'FLOAT'
    | 'HIO'
    | 'SIO'
    | 'TIO'
    ;

fragment F2_INSTR
    : 'ADDR'
    | 'SUBR'
    | 'COMPR'
    | 'MULR'
    | 'DIVR'
    | 'RMO'
    | 'SHIFTL'
    | 'SHIFTR'
    | 'SVC'
    | 'CLEAR'
    | 'TIXR'
    ;

fragment F34_INSTR
    : 'ADD'
    | 'ADDF'
    | 'AND'
    | 'COMP'
    | 'COMPF'
    | 'DIV'
    | 'DIVF'
    | 'J'
    | 'JEQ'
    | 'JGT'
    | 'JLT'
    | 'JSUB'
    | 'LDA'
    | 'LDB'
    | 'LDCH'
    | 'LDF'
    | 'LDL'
    | 'LDS'
    | 'LDT'
    | 'LDX'
    | 'MUL'
    | 'MULF'
    | 'MULR'
    | 'OR'
    | 'RD'
    | 'RSUB'
    | 'SSK'
    | 'STA'
    | 'STB'
    | 'STCH'
    | 'STF'
    | 'STI'
    | 'STL'
    | 'STS'
    | 'STSW'
    | 'STT'
    | 'STX'
    | 'SUB'
    | 'SUBF'
    | 'TIX'
    | 'WD'
    ;

F1CODOP  : F1_INSTR;
F2CODOP  : F2_INSTR;
F34CODOP : F34_INSTR;

// Nu?meros dec
NUM
    : ('0'..'9')+
    ;

// 0x...
CONSTHEX
    : '0x' ( '0'..'9' | 'a'..'f' | 'A'..'F' )+
    ;

// hex con sufijo H
HEXSUF
    : ( '0'..'9' | 'a'..'f' | 'A'..'F')+ ( 'h' | 'H')
    ;

// Cadena "..."
CONSTCAD
    : '"' ( '\\' . | ~('\"'|'\\') )* '"'
    ;

// ID
ID
    : ( 'a'..'z' | 'A'..'Z' | '_' )
      ( 'a'..'z' | 'A'..'Z' | '0'..'9' | '_' )*
    ;

// C'xxxx'
CHCONST
    : 'C' '\'' ( ~('\'' | '\r' | '\n') )* '\''
    ;

// X'F00'
HEXCONST
    : 'X' '\'' ( ('0'..'9') | ('a'..'f') | ('A'..'F') )+ '\''
    ;

// Ignorar
WS
    : (' ' | '\t')+ { $channel = Hidden; }
    ;