import base64
import logging
import os
import re

from urllib.parse import unquote
from requests_toolbelt.multipart import decoder
from github import Github
from github import Auth

TOKEN = os.getenv('TOKEN')
logger = logging.getLogger()
logger.setLevel(logging.DEBUG)

auth = Auth.Token(TOKEN)

g = Github(auth=auth)


def get_key(form_data):
    key = form_data.split(";")[1].split("=")[1].replace('"', '')
    return key


def handler(event, context):
    headers = event['headers']
    postdata = base64.b64decode(event['body'])
    request = {}  # Save request here

    for part in decoder.MultipartDecoder(postdata, headers['Content-Type']).parts:
        content = part.content.decode('utf-8')
        content = unquote(content)
        parts = re.split(r'&(user|time|version)=', content, flags=re.MULTILINE)
        logger.debug(parts)
        request['log'] = parts[0].replace('log=', '').replace('+', ' ')
        request['user'] = parts[2]
        request['time'] = parts[4]
        try:
            request['version'] = parts[6]
        except IndexError:
            logger.info('No version supplied by client.')
            request['version'] = 'Not given'
        logger.debug(request)
        logger.debug(content)

    if request['version'] == '0.0.0':
        logger.info('Detected Debug Version (0.0.0) skipping GitHub Ticket creation...')
        return {
            'statusCode': 400,
            'body': 'Debug build, crash report not created.'
        }

    current_repo = g.get_user().get_repo("VNGD-SimpleRadioStandalone")
    for issue in current_repo.get_issues():
        if issue.title == f'Crash by: {request["user"]} at {request["time"]}':
            logger.info('Issue already exists, skipping creation...')
            return {
                'statusCode': 200,
                'body': issue.html_url
            }
        if issue.body and request['log'] in issue.body:
            logger.info('Issue already exists, updating user list and count...')
            # Extract current users and count from the issue body
            user_list_pattern = r'Affected users:\n((?:- `.*?` times: `\d+`\n)+)'
            users = {}
            match = re.search(user_list_pattern, issue.body, re.MULTILINE)
            if match:
                for line in match.group(1).strip().split('\n'):
                    user_match = re.match(r'- `(.*?)` times: `(\d+)`', line)
                    if user_match:
                        users[user_match.group(1)] = int(user_match.group(2))
            # Update user count
            users[request['user']] = users.get(request['user'], 0) + 1
            # Build new user list
            user_lines = [f'- `{user}` times: `{count}`' for user, count in users.items()]
            new_body = re.sub(
                user_list_pattern,
                'Affected users:\n' + '\n'.join(user_lines) + '\n',
                issue.body,
                flags=re.MULTILINE
            )
            issue.edit(body=new_body)
            return {
                'statusCode': 200,
                'body': issue.html_url
            }
    logger.info('Creating new issue...')
    body = f"""Version: `{request['version']}`
Crash Log:
```
{request['log']}
```

Affected users:
- `{request['user']}` times: `1`

This is an automatic message and is only here so that the Development Team can work fast."""
    issue = current_repo.create_issue(
        title=f'Crash by: {request["user"]} at {request["time"]}',
        body=body,
        labels=['crash']
    )
    return {
        'statusCode': 200,
        'body': issue.html_url
    }
