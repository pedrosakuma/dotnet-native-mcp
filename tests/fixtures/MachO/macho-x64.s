.section __TEXT,__text,regular,pure_instructions
.globl _add
_add:
  movl %edi, %eax
  addl %esi, %eax
  retq
.globl _sub
_sub:
  movl %edi, %eax
  subl %esi, %eax
  retq
.section __DATA,__data
.globl _g
_g:
  .quad 42
.section __TEXT,__cstring,cstring_literals
_s:
  .asciz "hello-macho"
