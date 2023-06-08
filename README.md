# BWPackedXmlReader

PackedSection files are read-only data sections described in BNF format. The PackedSection file has the following format (the asterisk character —*— indicates that the previous section might appear zero or more times):

```
<packed_section> ::= <magic_number> <version> <string_table> <data_section>
<string_table> ::= <null_terminated_string>* '\0'
<data_section> ::= <num_children> <data_pos> <child_record>* <bin_data>
<child_record> ::= <key_pos> <data_pos>
<bin_data> ::= <bin_data_for_this_section> <bin_data_for_children>
<bin_data> ::= <bin_data_for_this_section>
```

The list below describes the sections in the file:
* <magic_number><br />
4-byte number 0x42A14E45 (0x62A14E45 in WoT)
* \<version\><br />
int8 number.
* <string_table><br />
Sequence of null-terminated strings, followed by an empty string.
In the section <key_pos>, these strings are referred to by their index in the table.
* <data_section><br />
Size of this section's data is indicated by the first <data_pos>.
In other words, all data from the start of <bin_data> through to the offset given by the first <data_pos>
is the data associated with this section.
<data_section> without children are just raw binary data for that type. For example, a float is four
bytes of the float, a Vector3 is 3 consecutive floats, a Matrix 12 float.
* <num_children><br />
int number.
* <data_pos><br />
int32 number, representing the offset relative to the start of section <bin_data> (and not as relative to
the start of the file).
The type of each section's data is indicated in the high bits of the following <data_pos> section (and
not in its own). This is the reason for the final <data_pos> after <child_record>.
* <bin_data><br />
Binary data block of the data for this section, and the data for each child section concatenated together.
* <child_record>
Data starts at the <data_pos> of the previous record (or the <data_pos> of the <data_section> if
this is the first record), and ends at the <data_pos> of this record.
* <key_pos><br />
int16 number, representing the index (and not a byte offset) in section <string_table> (relative to
the start of the file).
Index count starts at 0.
In PackedSection files, integers are slightly optimised (e.g., if value is zero, then no data will be stored; if
value fits in an int8, then 1 byte will be stored, and so on).
