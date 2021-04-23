# Remove all complete /* ... */ substrings in current line.
:complete
s/\/\*.*\*\///
t complete

# If no multiline comment opener - start new cycle with next line,
/\/\*/!b
# else:
#   /* ... found, so
#   concatenate next line to build complete comment;
N
#   restart cycle until ... */ will be appended and
#   complete comment /* ... */ will be removed.
b complete
