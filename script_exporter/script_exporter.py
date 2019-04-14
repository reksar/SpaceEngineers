"""
This script used as external tool in Visual Studio for export script file for
Space Engineers game.
"""

import sys
import os
import subprocess
from shutil import copyfile

SCRIPTS_ROOT = '..\\scripts'
DESTINATION_ROOT = 'C:\\Users\\reksar\\AppData\\Roaming\\SpaceEngineers\\IngameScripts\\local'
SCRIPT_FILENAME = 'Script.cs'

def extract_line_num(string):
    # string must look like: "28:    // INGAME ..."

    colon_pos = string.find(':')
    if colon_pos < 1:
        raise Exception('grep output has no line number')
    return int(string[:colon_pos])

def grep_file(filepath, pattern):
    grep_path = 'E:\\REKSAR\\soft\\portable\\git\\usr\\bin\\grep.exe'
    grep = [grep_path, '-n', pattern, filepath]
    out = subprocess.run(grep, stdout=subprocess.PIPE)
    return out.stdout.decode('utf-8')

def get_trimming_borders(script_file):

    # grep patterns
    START_OF_SCRIPT = '^\s*\/\/ INGAME SCRIPT START'
    END_OF_SCRIPT= '^\s*\/\/ INGAME SCRIPT END'

    matched_start_line = grep_file(script_file, START_OF_SCRIPT)
    start_border = extract_line_num(matched_start_line)

    matched_end_line = grep_file(script_file, END_OF_SCRIPT)
    end_border = extract_line_num(matched_end_line)

    return start_border - 1, end_border

def export_script(project_dir):

    destination_dir = '%s\\%s' % (DESTINATION_ROOT, project_dir)
    if not os.path.isdir(destination_dir):
        os.mkdir(destination_dir)

    destination_path = '%s\\%s' % (destination_dir, SCRIPT_FILENAME)
    if os.path.exists(destination_path):
        os.remove(destination_path)

    script_path = get_script_path(project_dir)
    script_file = open(script_path, 'r', errors='replace')
    script = script_file.readlines()
    script_file.close()

    trimming_start, trimming_end = get_trimming_borders(script_path)
    ingame_script = script[trimming_start:trimming_end]
    new_file = open(destination_path, 'w', errors='replace')
    new_file.writelines(ingame_script)
    new_file.close()

def get_script_path(project_dir):

    script_dir = '%s/%s' % (SCRIPTS_ROOT, project_dir)
    if not os.path.isdir(script_dir):
        msg = 'Project dir %s is not found.' % project_dir
        raise Exception(msg)

    script_path = '%s/%s' % (script_dir, SCRIPT_FILENAME)
    script_path = os.path.abspath(script_path)
    if not os.path.exists(script_path):
       msg = '%s file is not found in %s dir.' % (SCRIPT_FILENAME, project_dir)
       raise Exception(msg)
    return script_path

def get_project_dir():
    if len(sys.argv) > 1:
        path = sys.argv[1]
        path = os.path.normpath(path)
        return os.path.basename(path)
    return None

def main():
    project_dir = get_project_dir()
    print(project_dir)
    if project_dir:
        export_script(project_dir)
    else:
        print('There is no C# project dir specified.')

if __name__ == '__main__':
    main()
