# Temperatura na face oficial 77

Fonte: `incoming/0aa6b69626f7533b082918e10e7040ee.0` (119272 bytes,
ID 77, versão 0x0701). Os CRCs do pacote e dos quatro membros são válidos.
O módulo registra `dial_77` / `red_tick`. A miniatura mostra clima e
temperatura no topo, ao lado do dia da semana.

## Chamadas observadas

- `apph_cfg_weather_info_get(destino, 0)` lê os dados de clima mantidos pelo
  firmware. Em `dial_77.so`, a chamada está em `0x1918`, com o destino no
  estado da face +92. O segundo argumento é explicitamente zero.
- `get_local_time_from_utc0timestamps(destino, timestamp)` transforma a data
  de cada previsão em data local. Chamada em `0x193e`.
- `apph_cfg_unit_type_get()` fornece a unidade configurada. Retorno 2
  seleciona Fahrenheit; os demais retornos seguem o caminho Celsius.
- `sprintf` prepara o texto e `lv_label_set_text` o exibe. As strings reais
  do módulo incluem `%d℃`, `%d℉`, `--℃` e `--℉`.

A origem observada é o serviço de clima do firmware; o módulo não acessa
diretamente a internet. A análise não identifica a fonte meteorológica
usada pelo aplicativo do celular.

## Campos decodificados

O código percorre sete registros com passo de 48 bytes. Os deslocamentos
abaixo são relativos ao começo de **cada registro**, não ao estado da face:

| Deslocamento | Leitura observada | Uso |
|---|---|---|
| +1 | byte sem sinal | Código da condição do tempo, usado para escolher o ícone |
| +8 | inteiro de 16 bits com sinal | Temperatura em Celsius, incluindo valores negativos |
| +12 | inteiro de 32 bits | Timestamp UTC usado para identificar a data da previsão |
| +48 | início do próximo registro | Passo usado pelo laço de sete previsões |

Para a face 77, essas leituras aparecem no estado da face em +93, +100 e
+104, respectivamente, mais `indice * 48`. O estado tem 432 bytes; o
destino do getter começa em +92, deixando 340 bytes. O laço usa 336 bytes
para sete registros. Isso **não determina completamente** o tamanho e os
campos escritos pelo getter: há quatro bytes adicionais disponíveis no
chamador oficial. Ao implementar, não reservar apenas um registro ou os
campos conhecidos; manter uma área de saída conservadora e testar o ABI.

O módulo oficial `dial_100.so` confirma independentemente o mesmo passo e
os mesmos campos: destino em estado +172, temperatura em +180 e timestamp
em +184; seu estado tem 512 bytes, também deixando 340 bytes para o getter.

## Seleção e exibição

1. Obtém a hora/data atual com `get_local_time`.
2. A face 77 atualiza o cache de clima quando a hora muda. O byte de controle
   no estado +91 é inicializado com 255, forçando a primeira leitura.
3. Para cada um dos sete registros, converte o timestamp em data local e
   compara os primeiros quatro bytes da data com a data atual. Na estrutura
   usada pelo módulo, esses bytes representam ano (16 bits), mês e dia.
4. Ao encontrar o dia correspondente, lê a temperatura **com sinal** em +8.
5. Em Celsius, formata diretamente o valor. Em Fahrenheit, calcula
   `(temperatura_c * 9) / 5 + 32`, com divisão inteira com sinal, e formata
   com o símbolo de Fahrenheit.
6. Sem um registro para a data atual, exibe `--` com a unidade. Também usa
   uma imagem de clima sem dados.

O código da condição (+1) escolhe entre ícones de sol, nuvens, chuva, neve,
vento etc. Algumas condições também escolhem variantes de dia/noite pela
hora. A tabela completa de condições ainda não foi reconstruída.

## Limitação do editor na análise inicial

`SourceFor` em `editor/MainWindow.xaml.cs` não possui uma entrada para
`weatherTemperature`. Esse elemento importado recebe `source = "text"`;
portanto, o valor de amostra vira texto fixo. `build_live.py`, `clock.c` e a
lista de funções também ainda não têm uma fonte dinâmica de temperatura.

Para exibir temperatura real, é necessário acrescentar essa fonte ao
importador, à lista de funções e ao módulo, usando o getter de clima e a
seleção por data descritos acima. A fonte customizada deve incluir dígitos
e sinal negativo; a unidade pode ser um elemento separado. O dado ausente
deve aparecer como `--`, evitando confundir ausência de sincronização com
uma temperatura real de 0 graus.

Esta análise foi estática, sobre os módulos oficiais 77 e 100. Nenhuma
nova face foi enviada, e nenhuma implementação de temperatura foi
testada no relógio durante esta análise.

## Implementação posterior no editor

O editor agora oferece **Temperatura (clima)** e importa `weatherTemperature`
como `source = "temperature"`. O módulo seleciona a previsão do dia,
decodifica a temperatura com sinal e usa a unidade configurada no relógio.
Sem previsão correspondente, mostra `--`; o valor zero é preservado.
O texto numérico usa quatro células, com a mesma rasterização da prévia.
O símbolo da unidade pode ser um elemento separado.

O getter recebe 384 bytes alocados no heap e liberados depois da leitura;
o valor decodificado é atualizado quando muda a data ou a hora. Testes
ARM com serviços simulados cobrem datas, valores negativos, conversão,
ausência de dados, falha de alocação e limites de saída. A geração e a
validação local do pacote passaram. A implementação ainda não foi
enviada nem validada no relógio.
